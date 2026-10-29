/*
[ 코드 설명 ]

서버 프로그램의 시작점

1. 7777 포트에서 GameServer를 실행한다.
2. Ctrl+C를 종료 요청으로 바꾸어 연결과 비동기 작업을 정리한다.
3. 서버 시작 및 실행 오류를 출력하고 실패 종료 코드를 반환한다.
*/

using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
using var stopping = new CancellationTokenSource();

ConsoleCancelEventHandler cancelHandler = (_, e) =>
{
    e.Cancel = true; // 즉시 프로세스를 끝내지 않고 서버의 finally가 실행되게 한다.
    stopping.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    var server = new GameServer();
    var rooms = new RoomManager(server);
    server.MessageHandler = rooms.HandleMessageAsync;
    server.ClientDisconnected += rooms.HandleClientDisconnected;
    await server.StartAsync(7777, stopping.Token);
}
catch (Exception e)
{
    Console.Error.WriteLine($"[SERVER ERROR] {e}");
    Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
