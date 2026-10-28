/*
[ 코드 설명 ]

TCP 테스트 클라이언트

1. 같은 컴퓨터에서 실행 중인 127.0.0.1:7777 서버에 접속한다.
2. 콘솔에서 문자열을 입력받는다.
3. 입력한 문자열을 서버에 전송한다.
4. 서버가 보내준 응답을 콘솔에 출력한다.
5. EXIT 입력 시 클라이언트를 종료한다.

Unity와 서버를 연결하기 전에
TCP Socket 통신이 정상적으로 동작하는지 확인하기 위한 프로그램이다.
*/

using System.Net;
using System.Net.Sockets;
using System.Text;

Socket clientSocket = new Socket(
    AddressFamily.InterNetwork,
    SocketType.Stream,
    ProtocolType.Tcp
);

IPEndPoint serverEndPoint = new IPEndPoint(
    IPAddress.Parse("127.0.0.1"),
    7777
);

Console.WriteLine("==============================");
Console.WriteLine("       Test Client");
Console.WriteLine("==============================");
Console.WriteLine();

Console.WriteLine("서버에 접속 중...");

await clientSocket.ConnectAsync(
    serverEndPoint
);

Console.WriteLine("서버 접속 성공!");
Console.WriteLine();
Console.WriteLine("메시지를 입력하세요.");
Console.WriteLine("종료하려면 EXIT 입력");
Console.WriteLine();

byte[] buffer = new byte[4096];

while (true)
{
    Console.Write("> ");

    string? message = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(message))
    {
        continue;
    }

    if (message.ToUpper() == "EXIT")
    {
        break;
    }

    byte[] sendData =
        Encoding.UTF8.GetBytes(message);

    await clientSocket.SendAsync(
        sendData,
        SocketFlags.None
    );

    int receivedBytes =
        await clientSocket.ReceiveAsync(
            buffer,
            SocketFlags.None
        );

    string response =
        Encoding.UTF8.GetString(
            buffer,
            0,
            receivedBytes
        );

    Console.WriteLine(
        $"Server : {response}"
    );
}

try
{
    clientSocket.Shutdown(
        SocketShutdown.Both
    );
}
catch
{
}

clientSocket.Close();

Console.WriteLine();
Console.WriteLine("클라이언트 종료");