#include "tcp_connection.h"

#include <stdlib.h>
#include <string.h>

/* ---------- 플랫폼 차이 감추기 ---------- */
#ifdef _WIN32
#include <windows.h>
typedef CRITICAL_SECTION tcp_mutex;
static void mutex_init(tcp_mutex *m)    { InitializeCriticalSection(m); }
static void mutex_destroy(tcp_mutex *m) { DeleteCriticalSection(m); }
static void mutex_lock(tcp_mutex *m)    { EnterCriticalSection(m); }
static void mutex_unlock(tcp_mutex *m)  { LeaveCriticalSection(m); }
#define SEND_FLAGS 0
#define SHUT_BOTH  SD_BOTH
static int  last_error_is_intr(void)    { return 0; }
static int  last_error_is_timeout(void) { return WSAGetLastError() == WSAETIMEDOUT; }
#else
#include <errno.h>
#include <pthread.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>
typedef pthread_mutex_t tcp_mutex;
static void mutex_init(tcp_mutex *m)    { pthread_mutex_init(m, NULL); }
static void mutex_destroy(tcp_mutex *m) { pthread_mutex_destroy(m); }
static void mutex_lock(tcp_mutex *m)    { pthread_mutex_lock(m); }
static void mutex_unlock(tcp_mutex *m)  { pthread_mutex_unlock(m); }
#ifdef MSG_NOSIGNAL
#define SEND_FLAGS MSG_NOSIGNAL /* 끊긴 소켓에 써도 SIGPIPE로 프로세스가 죽지 않게 한다. */
#else
#define SEND_FLAGS 0
#endif
#define SHUT_BOTH SHUT_RDWR
#define closesocket close
static int last_error_is_intr(void)    { return errno == EINTR; }
static int last_error_is_timeout(void) { return errno == EAGAIN || errno == EWOULDBLOCK; }
#endif

struct tcp_connection {
    tcp_socket_t sock;
    tcp_mutex    send_lock;   /* C#의 SemaphoreSlim(1,1): 송신 직렬화 */
    tcp_mutex    state_lock;  /* C#의 sendStateLock: disposed 보호 */
    int          disposed;
    int          sock_closed;

    /* 한 번의 recv에 여러 메시지가 와도 남은 바이트를 다음 호출에 보존한다. */
    unsigned char receive_buffer[4096];
    unsigned char message_buffer[TCP_MAX_MESSAGE_BYTES];
    size_t        receive_offset;
    size_t        receive_count;
};

static int is_disposed(tcp_connection *c)
{
    int d;
    mutex_lock(&c->state_lock);
    d = c->disposed;
    mutex_unlock(&c->state_lock);
    return d;
}

tcp_connection *tcp_connection_create(tcp_socket_t sock)
{
    tcp_connection *c = (tcp_connection *)calloc(1, sizeof *c);
    if (!c) return NULL;
    c->sock = sock;
    mutex_init(&c->send_lock);
    mutex_init(&c->state_lock);

    /* C#의 SendTimeout(10초). 소켓에 걸어 두면 send가 그 이상 막히지 않는다. */
#ifdef _WIN32
    DWORD tv = TCP_SEND_TIMEOUT_MS;
#else
    struct timeval tv;
    tv.tv_sec = TCP_SEND_TIMEOUT_MS / 1000;
    tv.tv_usec = (TCP_SEND_TIMEOUT_MS % 1000) * 1000;
#endif
    setsockopt(sock, SOL_SOCKET, SO_SNDTIMEO, (const char *)&tv, sizeof tv);
    return c;
}

int tcp_utf8_valid(const unsigned char *s, size_t n)
{
    size_t i = 0;
    while (i < n) {
        unsigned char b = s[i];
        size_t need, k;
        unsigned long cp;
        if (b < 0x80) { i++; continue; }
        if (b >= 0xC2 && b <= 0xDF)      { need = 1; cp = b & 0x1F; }
        else if (b >= 0xE0 && b <= 0xEF) { need = 2; cp = b & 0x0F; } /* 한글은 여기(3바이트) */
        else if (b >= 0xF0 && b <= 0xF4) { need = 3; cp = b & 0x07; }
        else return 0;
        if (n - i - 1 < need) return 0;
        for (k = 1; k <= need; k++) {
            unsigned char cb = s[i + k];
            if ((cb & 0xC0) != 0x80) return 0;
            cp = (cp << 6) | (cb & 0x3F);
        }
        if (need == 2 && (cp < 0x800 || (cp >= 0xD800 && cp <= 0xDFFF))) return 0;
        if (need == 3 && (cp < 0x10000 || cp > 0x10FFFF)) return 0;
        i += need + 1;
    }
    return 1;
}

tcp_status tcp_receive(tcp_connection *c, char *out, size_t out_cap, size_t *out_len)
{
    size_t message_length = 0;
    if (!c || !out || out_cap < TCP_MAX_MESSAGE_BYTES + 1) return TCP_ERR_ARG;
    if (out_len) *out_len = 0;

    for (;;) {
        unsigned char value;

        if (c->receive_offset == c->receive_count) {
            int n = recv(c->sock, (char *)c->receive_buffer, (int)sizeof c->receive_buffer, 0);
            /* 로컬에서 tcp_close로 깨운 EOF를 미완성 메시지 오류로 오인하지 않는다. */
            if (is_disposed(c)) return TCP_CLOSED;
            if (n < 0) {
                if (last_error_is_intr()) continue;
                return TCP_ERR_IO;
            }
            c->receive_offset = 0;
            c->receive_count = (size_t)n;
            if (n == 0) {
                return message_length != 0 ? TCP_ERR_PARTIAL : TCP_CLOSED;
            }
        }

        /* TCP recv 횟수는 메시지 개수와 무관하다. LF를 만날 때까지 직접 경계를 찾는다. */
        value = c->receive_buffer[c->receive_offset++];
        if (value == '\n') {
            /* 완성된 바이트를 한 번에 검사하므로 한글이 recv 중간에 잘려도 안전하다. */
            if (!tcp_utf8_valid(c->message_buffer, message_length)) return TCP_ERR_UTF8;
            memcpy(out, c->message_buffer, message_length);
            out[message_length] = '\0';
            if (out_len) *out_len = message_length;
            return TCP_OK;
        }
        if (value == '\r') return TCP_ERR_CR;
        if (message_length == TCP_MAX_MESSAGE_BYTES) return TCP_ERR_TOO_LONG;
        c->message_buffer[message_length++] = value;
    }
}

tcp_status tcp_send(tcp_connection *c, const char *msg, size_t len)
{
    unsigned char data[TCP_MAX_MESSAGE_BYTES + 1];
    size_t total, sent = 0;
    tcp_status status = TCP_OK;

    if (!c || (!msg && len)) return TCP_ERR_ARG;
    if (memchr(msg, '\n', len) || memchr(msg, '\r', len)) return TCP_ERR_ARG;
    if (len > TCP_MAX_MESSAGE_BYTES) return TCP_ERR_TOO_LONG;
    /* C# string은 항상 유효한 문자열이지만 C의 char*는 아무 바이트나 될 수 있어 직접 검사한다. */
    if (!tcp_utf8_valid((const unsigned char *)msg, len)) return TCP_ERR_UTF8;

    memcpy(data, msg, len);
    data[len] = '\n';
    total = len + 1;

    if (is_disposed(c)) return TCP_ERR_DISPOSED;

    mutex_lock(&c->send_lock);
    while (sent < total) {
        /* 한 번에 전부 전송된다는 보장이 없으므로 남은 부분을 반복한다. */
        int n = send(c->sock, (const char *)data + sent, (int)(total - sent), SEND_FLAGS);
        if (n < 0) {
            if (last_error_is_intr()) continue;
            status = last_error_is_timeout() ? TCP_ERR_TIMEOUT : TCP_ERR_IO;
            break;
        }
        if (n == 0) { status = TCP_ERR_IO; break; }
        sent += (size_t)n;
    }
    mutex_unlock(&c->send_lock);

    /* 프레임 일부만 보낸 뒤 실패하면 경계가 깨지므로 연결을 닫는다. */
    if (status != TCP_OK) tcp_close(c);
    return status;
}

void tcp_close(tcp_connection *c)
{
    if (!c) return;
    mutex_lock(&c->state_lock);
    if (c->disposed) { mutex_unlock(&c->state_lock); return; }
    c->disposed = 1;
    mutex_unlock(&c->state_lock);

    /* 실패해도(미접속, 이미 끊김) 무시한다. 막혀 있는 recv/send가 깨어난다. */
    shutdown(c->sock, SHUT_BOTH);
#ifdef _WIN32
    /* Windows는 shutdown만으로 블로킹 recv가 안 깨어날 수 있어 핸들을 바로 닫는다. */
    mutex_lock(&c->state_lock);
    if (!c->sock_closed) { closesocket(c->sock); c->sock_closed = 1; }
    mutex_unlock(&c->state_lock);
#endif
    /* POSIX에서는 여기서 close하지 않는다. 다른 스레드가 recv 중일 때 fd 번호가
       재사용되면 엉뚱한 소켓을 읽게 되므로, 실제 close는 tcp_free에서 한다. */
}

void tcp_free(tcp_connection *c)
{
    if (!c) return;
    tcp_close(c);
    if (!c->sock_closed) { closesocket(c->sock); c->sock_closed = 1; }
    mutex_destroy(&c->send_lock);
    mutex_destroy(&c->state_lock);
    free(c);
}
