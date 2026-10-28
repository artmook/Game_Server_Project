/*
[ 코드 설명 ]

GameServer

1. TCP Socket 서버를 생성한다.
2. 7777 포트에서 클라이언트의 접속을 기다린다.
3. 클라이언트가 접속하면 각각 별도의 Task로 처리한다.
4. 클라이언트가 보낸 문자열을 서버 콘솔에 출력한다.
5. 받은 문자열을 확인했다는 응답을 다시 클라이언트에게 전송한다.

현재 단계에서는 게임 로직을 구현하지 않고
서버와 클라이언트의 기본 TCP 통신이 가능한지만 확인한다.
*/

using System.Net;
using System.Net.Sockets;
using System.Text;

public class GameServer
{
    private int nextClientId = 1;

    public async Task StartAsync(int port)
    {
        Socket serverSocket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );

        IPEndPoint endPoint = new IPEndPoint(
            IPAddress.Any,
            port
        );

        serverSocket.Bind(endPoint);
        serverSocket.Listen(10);

        Console.WriteLine("==============================");
        Console.WriteLine("       Game Server Start");
        Console.WriteLine("==============================");
        Console.WriteLine($"Port : {port}");
        Console.WriteLine();
        Console.WriteLine("클라이언트 접속 대기 중...");
        Console.WriteLine();

        while (true)
        {
            Socket clientSocket =
                await serverSocket.AcceptAsync();

            int clientId = nextClientId++;

            Console.WriteLine(
                $"[CONNECT] Client {clientId} 접속"
            );

            _ = Task.Run(
                () => HandleClientAsync(
                    clientSocket,
                    clientId
                )
            );
        }
    }

    private async Task HandleClientAsync(
        Socket clientSocket,
        int clientId
    )
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (true)
            {
                int receivedBytes =
                    await clientSocket.ReceiveAsync(
                        buffer,
                        SocketFlags.None
                    );

                if (receivedBytes == 0)
                {
                    break;
                }

                string message =
                    Encoding.UTF8.GetString(
                        buffer,
                        0,
                        receivedBytes
                    );

                Console.WriteLine(
                    $"[RECV] Client {clientId} : {message}"
                );

                string response =
                    $"SERVER|RECEIVED|{message}";

                byte[] responseData =
                    Encoding.UTF8.GetBytes(response);

                await clientSocket.SendAsync(
                    responseData,
                    SocketFlags.None
                );
            }
        }
        catch (SocketException e)
        {
            Console.WriteLine(
                $"[ERROR] Client {clientId} : {e.Message}"
            );
        }
        finally
        {
            Console.WriteLine(
                $"[DISCONNECT] Client {clientId}"
            );

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
        }
    }
}