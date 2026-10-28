/*
[ 코드 설명 ]

TcpConnection

1. Server와 TestClient가 동일하게 사용하는 UTF-8 TCP 송수신 규격을 정의한다.
2. LF(줄바꿈)까지 받은 바이트를 모아 완성된 메시지만 반환한다.
3. 일부만 전송되면 남은 데이터를 전송하고 같은 소켓의 송신을 직렬화한다.
4. 메시지 크기, 잘못된 UTF-8, 전송 지연을 검사하고 소켓을 정리한다.
*/

using System.Net.Sockets;
using System.Text;

namespace GameServerProject.Networking;

public sealed class TcpConnection : IDisposable
{
    // LF를 제외한 한 메시지의 바이트 제한이다. 본문에 CR 또는 LF는 허용하지 않는다.
    // 서버가 ID와 명령 접두사를 붙일 공간을 남기기 위해 클라이언트 입력은 더 작게 제한한다.
    public const int MaxMessageBytes = 8192;
    public const int MaxClientMessageBytes = 4096;
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding Utf8 = new(false, true); // 잘못된 문자를 조용히 대체하지 않는다.

    private readonly Socket socket;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly object sendStateLock = new();
    private int activeSends;
    private bool disposed;

    // 한 번의 수신에 여러 메시지가 와도 남은 바이트를 다음 호출에 보존한다.
    // ReceiveAsync는 연결마다 하나의 수신 루프에서만 호출해야 한다.
    private readonly byte[] receiveBuffer = new byte[4096];
    private readonly byte[] messageBuffer = new byte[MaxMessageBytes];
    private int receiveOffset;
    private int receiveCount;

    public TcpConnection(Socket socket)
    {
        this.socket = socket;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        int messageLength = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (receiveOffset == receiveCount)
            {
                receiveCount = await socket.ReceiveAsync(receiveBuffer.AsMemory(), SocketFlags.None, cancellationToken);
                receiveOffset = 0;
                // 로컬 종료로 소켓을 닫을 때 반환된 EOF를 미완성 메시지 오류로 오인하지 않는다.
                cancellationToken.ThrowIfCancellationRequested();
                if (receiveCount == 0)
                {
                    if (messageLength != 0)
                    {
                        throw new InvalidDataException("메시지 끝의 LF를 받기 전에 연결이 종료되었습니다.");
                    }
                    return null;
                }
            }

            // TCP 수신 횟수는 메시지 개수와 무관하다. LF를 만날 때까지 직접 경계를 찾는다.
            byte value = receiveBuffer[receiveOffset++];
            if (value == (byte)'\n')
            {
                try
                {
                    // 완성된 바이트를 한 번에 해석하므로 한글이 수신 중 잘려도 안전하다.
                    return Utf8.GetString(messageBuffer, 0, messageLength);
                }
                catch (DecoderFallbackException e)
                {
                    throw new InvalidDataException("올바른 UTF-8 메시지가 아닙니다.", e);
                }
            }
            if (value == (byte)'\r')
            {
                throw new InvalidDataException("CR은 허용하지 않습니다. 메시지는 LF로 끝내세요.");
            }
            if (messageLength == MaxMessageBytes)
            {
                throw new InvalidDataException($"메시지가 최대 {MaxMessageBytes}바이트를 초과했습니다.");
            }
            messageBuffer[messageLength++] = value;
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Contains('\n') || message.Contains('\r'))
        {
            throw new ArgumentException("메시지 본문에 CR 또는 LF를 넣을 수 없습니다.", nameof(message));
        }
        int byteCount = Utf8.GetByteCount(message);
        if (byteCount > MaxMessageBytes)
        {
            throw new ArgumentException($"메시지는 최대 {MaxMessageBytes}바이트입니다.", nameof(message));
        }

        byte[] data = new byte[byteCount + 1];
        Utf8.GetBytes(message.AsSpan(), data.AsSpan());
        data[^1] = (byte)'\n';

        // 정리와 송신 시작의 경합을 막는다. 대기 중인 송신까지 센 뒤 마지막 작업이
        // 끝날 때 SemaphoreSlim을 정리해야 Dispose와 WaitAsync가 충돌하지 않는다.
        lock (sendStateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeSends++;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            await sendLock.WaitAsync(timeout.Token);
            try
            {
                int sent = 0;
                while (sent < data.Length)
                {
                    // 한 번에 전체가 전송된다는 보장이 없으므로 남은 부분을 반복한다.
                    int count = await socket.SendAsync(data.AsMemory(sent), SocketFlags.None, timeout.Token);
                    if (count == 0)
                    {
                        throw new SocketException((int)SocketError.ConnectionReset);
                    }
                    sent += count;
                }
            }
            catch
            {
                // 프레임의 일부만 보낸 뒤 실패하면 재사용 시 경계가 깨지므로 연결을 닫는다.
                Dispose();
                throw;
            }
            finally
            {
                sendLock.Release();
            }
        }
        finally
        {
            lock (sendStateLock)
            {
                activeSends--;
                if (disposed && activeSends == 0)
                {
                    sendLock.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (sendStateLock)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (activeSends == 0)
            {
                sendLock.Dispose();
            }
        }

        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // 미접속 또는 이미 끊어진 소켓도 Dispose는 반드시 수행한다.
        }
        catch (ObjectDisposedException)
        {
            // 이미 닫힌 소켓의 중복 정리는 허용한다.
        }
        finally
        {
            socket.Dispose(); // 진행 중인 수신도 깨워 수신 작업의 finally가 실행되게 한다.
        }
    }
}
