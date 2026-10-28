/*
[ 코드 설명 ]

서버 프로그램의 시작점

1. GameServer 객체를 생성한다.
2. 7777 포트를 사용하여 TCP 서버를 실행한다.
*/

GameServer server = new GameServer();

await server.StartAsync(7777);