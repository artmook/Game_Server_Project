/*
[ 코드 설명 ]

tcp_connection (C# TcpConnection의 C 버전)

1. Server와 TestClient가 같이 쓰는 UTF-8 + LF 구분 TCP 송수신 규격을 C로 구현한다.
2. LF까지 받은 바이트를 모아 완성된 메시지만 돌려준다.
3. 일부만 전송되면 나머지를 이어서 보내고, 한 소켓의 송신은 mutex로 직렬화한다.
4. 메시지 크기, 잘못된 UTF-8, 전송 지연(SO_SNDTIMEO)을 검사하고 소켓을 정리한다.

C#과 다른 점
- 예외 대신 tcp_status 반환값으로 오류를 알린다.
- GC가 없어서 "연결 끊기(tcp_close)"와 "메모리 해제(tcp_free)"를 나눈다.
  tcp_close는 아무 스레드에서나 여러 번 불러도 되고, 막혀 있는 recv/send를 깨운다.
  tcp_free는 수신/송신 스레드가 모두 끝난 뒤 소유자가 딱 한 번 부른다.
*/
#ifndef TCP_CONNECTION_H
#define TCP_CONNECTION_H

#include <stddef.h>

#ifdef _WIN32
#include <winsock2.h>
typedef SOCKET tcp_socket_t;
#else
typedef int tcp_socket_t;
#endif

/* LF를 뺀 한 메시지의 최대 바이트. 클라이언트 입력은 서버 접두사 공간을 위해 더 작게 제한한다. */
#define TCP_MAX_MESSAGE_BYTES        8192
#define TCP_MAX_CLIENT_MESSAGE_BYTES 4096
#define TCP_SEND_TIMEOUT_MS          10000

typedef enum {
    TCP_OK            = 0,
    TCP_CLOSED        = 1,   /* 정상 종료 (C#의 null 반환) */
    TCP_ERR_ARG       = -1,  /* 잘못된 인자, 본문에 CR/LF 포함 (ArgumentException) */
    TCP_ERR_PARTIAL   = -2,  /* LF 전에 연결이 끊김 */
    TCP_ERR_CR        = -3,  /* 수신 데이터에 CR 포함 */
    TCP_ERR_TOO_LONG  = -4,  /* 최대 크기 초과 */
    TCP_ERR_UTF8      = -5,  /* 잘못된 UTF-8 */
    TCP_ERR_IO        = -6,  /* 소켓 오류 */
    TCP_ERR_TIMEOUT   = -7,  /* 송신 시간 초과 */
    TCP_ERR_DISPOSED  = -8   /* 이미 닫힌 연결 (ObjectDisposedException) */
} tcp_status;

typedef struct tcp_connection tcp_connection;

/* 연결된 소켓의 소유권을 가져온다. 실패하면 NULL. */
tcp_connection *tcp_connection_create(tcp_socket_t sock);

/* 메시지 하나를 받는다. 연결마다 하나의 수신 스레드에서만 호출한다.
   out_cap은 TCP_MAX_MESSAGE_BYTES + 1 이상이어야 하고, 결과는 '\0'으로 끝난다. */
tcp_status tcp_receive(tcp_connection *c, char *out, size_t out_cap, size_t *out_len);

/* 메시지 하나를 보낸다(LF는 자동으로 붙음). 여러 스레드에서 동시에 불러도 된다. */
tcp_status tcp_send(tcp_connection *c, const char *msg, size_t len);

/* C#의 Dispose: 연결을 끊고 막혀 있는 recv/send를 깨운다. 여러 번 불러도 안전하다. */
void tcp_close(tcp_connection *c);

/* 소켓과 메모리를 해제한다. 이 연결을 쓰는 스레드가 모두 끝난 뒤 한 번만 부른다. */
void tcp_free(tcp_connection *c);

/* 엄격한 UTF-8 검사(과잉 길이, 서로게이트, U+10FFFF 초과 거부). 올바르면 1. */
int tcp_utf8_valid(const unsigned char *s, size_t n);

#endif
