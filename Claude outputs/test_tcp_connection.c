/* Linux/macOS용 간단 테스트: socketpair로 양 끝을 만들어 규격을 확인한다. */
#include "tcp_connection.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>

static char buf[TCP_MAX_MESSAGE_BYTES + 1];

static void pair(int fds[2]) { assert(socketpair(AF_UNIX, SOCK_STREAM, 0, fds) == 0); }

int main(void)
{
    int fds[2];
    size_t len;
    tcp_connection *a, *b;

    /* 1) 한 번에 여러 메시지 + 한글 + 쪼개진 전송 */
    pair(fds);
    a = tcp_connection_create(fds[0]);
    b = tcp_connection_create(fds[1]);
    assert(tcp_send(a, "안녕", strlen("안녕")) == TCP_OK);
    write(fds[0], "hello\nwor", 9);
    write(fds[0], "\xED\x95", 2);       /* '한'의 앞 2바이트 */
    write(fds[0], "\x9C" "ld\n", 4);    /* 나머지 */
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_OK && strcmp(buf, "안녕") == 0);
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_OK && strcmp(buf, "hello") == 0);
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_OK && strcmp(buf, "wor한ld") == 0);

    /* 2) 송신 인자 검사 */
    assert(tcp_send(a, "a\nb", 3) == TCP_ERR_ARG);
    assert(tcp_send(a, "\xFF", 1) == TCP_ERR_UTF8);

    /* 3) 수신 오류: CR, 잘못된 UTF-8 */
    write(fds[0], "x\r\n", 3);
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_ERR_CR);
    tcp_free(a); tcp_free(b);

    pair(fds);
    b = tcp_connection_create(fds[1]);
    write(fds[0], "\xC0\xAF\n", 3);     /* 과잉 길이 '/' */
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_ERR_UTF8);
    tcp_free(b); close(fds[0]);

    /* 4) LF 전에 끊김 / 정상 종료 */
    pair(fds);
    b = tcp_connection_create(fds[1]);
    write(fds[0], "abc", 3); close(fds[0]);
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_ERR_PARTIAL);
    tcp_free(b);

    pair(fds);
    b = tcp_connection_create(fds[1]);
    close(fds[0]);
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_CLOSED);
    tcp_free(b);

    /* 5) 최대 크기 초과 */
    pair(fds);
    b = tcp_connection_create(fds[1]);
    {
        static char big[TCP_MAX_MESSAGE_BYTES + 2];
        memset(big, 'a', sizeof big - 1);
        big[sizeof big - 1] = '\n';
        write(fds[0], big, sizeof big);
    }
    assert(tcp_receive(b, buf, sizeof buf, &len) == TCP_ERR_TOO_LONG);
    tcp_free(b); close(fds[0]);

    /* 6) 닫힌 연결에 송신 */
    pair(fds);
    a = tcp_connection_create(fds[0]);
    tcp_close(a);
    assert(tcp_send(a, "x", 1) == TCP_ERR_DISPOSED);
    tcp_free(a); close(fds[1]);

    puts("all tests passed");
    return 0;
}
