/*
[ 코드 설명 ]

ServerConnection (Unity용 TCP 연결)

1. 서버와 같은 규격(BOM 없는 UTF-8 + LF, 본문 CR/LF 금지)으로 송수신한다.
2. 수신과 송신은 각각 백그라운드 스레드에서 하고, 받은 메시지는 큐에 쌓는다.
3. Unity API는 메인 스레드에서만 쓸 수 있으므로 NetworkGameManager가 Update에서 큐를 꺼낸다.
4. 송신은 큐 순서대로 한 스레드가 보내므로 메시지 경계와 순서가 섞이지 않는다.
*/

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameServer
{
    public enum ConnectionEventType { Connected, Message, Disconnected }

    public readonly struct ConnectionEvent
    {
        public readonly ConnectionEventType Type;
        public readonly string Text; // Message: 본문, Disconnected: 종료 사유

        public ConnectionEvent(ConnectionEventType type, string text)
        {
            Type = type;
            Text = text;
        }
    }

    public sealed class ServerConnection : IDisposable
    {
        public const int MaxMessageBytes = 8192;       // 서버 TcpConnection.MaxMessageBytes
        public const int MaxClientMessageBytes = 4096; // 서버 TcpConnection.MaxClientMessageBytes
        private const int SendTimeoutMs = 10000;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true); // 잘못된 바이트는 예외

        private readonly ConcurrentQueue<ConnectionEvent> events = new ConcurrentQueue<ConnectionEvent>();
        private readonly BlockingCollection<byte[]> sendQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        private readonly object gate = new object();
        private TcpClient client;
        private NetworkStream stream;
        private string failure;
        private int closed;
        private int disconnectReported;

        public bool IsClosed => Volatile.Read(ref closed) == 1;

        public void Connect(string host, int port)
        {
            var thread = new Thread(() => Run(host, port)) { IsBackground = true, Name = "ServerConnection.Receive" };
            thread.Start();
        }

        // 메인 스레드에서 매 프레임 호출한다.
        public bool TryDequeue(out ConnectionEvent value) => events.TryDequeue(out value);

        // 본문 검사 후 송신 큐에 넣는다. 연결이 닫혔으면 false.
        public bool Send(string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.IndexOf('\n') >= 0 || message.IndexOf('\r') >= 0)
                throw new ArgumentException("메시지 본문에 CR 또는 LF를 넣을 수 없습니다.", nameof(message));
            int byteCount = Utf8.GetByteCount(message);
            if (byteCount > MaxClientMessageBytes)
                throw new ArgumentException($"클라이언트 메시지는 최대 {MaxClientMessageBytes}바이트입니다.", nameof(message));

            byte[] data = new byte[byteCount + 1];
            Utf8.GetBytes(message, 0, message.Length, data, 0);
            data[byteCount] = (byte)'\n';
            if (IsClosed) return false;
            try
            {
                return sendQueue.TryAdd(data);
            }
            catch (InvalidOperationException)
            {
                return false; // Close와 경합해 이미 송신을 마감한 경우이다.
            }
        }

        private void Run(string host, int port)
        {
            try
            {
                var connecting = new TcpClient { NoDelay = true, SendTimeout = SendTimeoutMs };
                connecting.Connect(host, port);
                lock (gate)
                {
                    if (IsClosed)
                    {
                        connecting.Close();
                        return;
                    }
                    client = connecting;
                    stream = connecting.GetStream();
                }
                new Thread(SendLoop) { IsBackground = true, Name = "ServerConnection.Send" }.Start();
                events.Enqueue(new ConnectionEvent(ConnectionEventType.Connected, null));
                ReceiveLoop();
            }
            catch (Exception e)
            {
                if (!IsClosed) failure = failure ?? e.Message;
            }
            finally
            {
                Close();
                ReportDisconnected();
            }
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            byte[] message = new byte[MaxMessageBytes];
            int length = 0;
            while (true)
            {
                int count = stream.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    if (length != 0) throw new InvalidDataException("메시지 끝의 LF를 받기 전에 연결이 종료되었습니다.");
                    return;
                }
                // TCP 수신 횟수와 메시지 개수는 무관하다. LF를 만날 때까지 직접 모은다.
                for (int i = 0; i < count; i++)
                {
                    byte value = buffer[i];
                    if (value == (byte)'\n')
                    {
                        string text;
                        try
                        {
                            text = Utf8.GetString(message, 0, length); // 한글이 수신 중 잘려도 완성 후 해석한다.
                        }
                        catch (DecoderFallbackException e)
                        {
                            throw new InvalidDataException("올바른 UTF-8 메시지가 아닙니다.", e);
                        }
                        events.Enqueue(new ConnectionEvent(ConnectionEventType.Message, text));
                        length = 0;
                        continue;
                    }
                    if (value == (byte)'\r') throw new InvalidDataException("CR은 허용하지 않습니다.");
                    if (length == MaxMessageBytes) throw new InvalidDataException($"메시지가 최대 {MaxMessageBytes}바이트를 초과했습니다.");
                    message[length++] = value;
                }
            }
        }

        private void SendLoop()
        {
            try
            {
                foreach (byte[] data in sendQueue.GetConsumingEnumerable())
                    stream.Write(data, 0, data.Length); // 부분 전송은 Write가 끝까지 처리한다.
            }
            catch (Exception e)
            {
                if (!IsClosed) failure = failure ?? e.Message;
                Close(); // 프레임 일부만 보냈을 수 있으므로 연결을 재사용하지 않는다.
            }
        }

        private void ReportDisconnected()
        {
            if (Interlocked.Exchange(ref disconnectReported, 1) == 1) return;
            events.Enqueue(new ConnectionEvent(ConnectionEventType.Disconnected, failure ?? "연결 종료"));
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref closed, 1) == 1) return;
            sendQueue.CompleteAdding();
            lock (gate)
            {
                // 소켓을 닫으면 막혀 있는 Read/Write가 예외로 깨어나 스레드가 끝난다.
                client?.Close();
            }
        }

        public void Dispose() => Close();
    }
}
