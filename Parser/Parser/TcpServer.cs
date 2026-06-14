using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Parser
{
    public class TcpServer : IDisposable
    {
        private Socket? _serverSocket;
        private readonly CancellationTokenSource _cts;

        private const int _BUFFERSIZE = 4096;

        private readonly IPAddress _address;
        private readonly int _port;

        public TcpServer()
        {
            _address = IPAddress.Loopback;
            _port = 8080;
            _cts = new ();
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var endpoint = new IPEndPoint(_address, _port);
            _serverSocket.Bind(endpoint);
            _serverSocket.Listen();

            Console.WriteLine($"TcpServer запущен");

            while (!cancellationToken.IsCancellationRequested)
            {
                Socket clientSocket;
                try
                {
                    clientSocket = await _serverSocket.AcceptAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    Console.Error.WriteLine($"Ошибка: {ex.Message}");
                    continue;
                }

                Console.WriteLine($"Клиент подключен: {clientSocket.RemoteEndPoint}");

                _ = ProcessClientAsync(clientSocket, cancellationToken);
            }

            Console.WriteLine("TcpServer остановлен");
        }

        private static async Task ProcessClientAsync(Socket clientSocket, CancellationToken cancellationToken)
        {
            var remoteEndpoint = clientSocket.RemoteEndPoint?.ToString();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(_BUFFERSIZE);

            try
            {
                var memory = new Memory<byte>(buffer, 0, _BUFFERSIZE);

                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = await clientSocket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException ex)
                    {
                        Console.Error.WriteLine($"Ошибка получения данных от клиента: {ex.Message}");
                        break;
                    }

                    //Клиент закрыл соединение
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"{remoteEndpoint} отсоединен.");
                        break;
                    }

                    ReadOnlySpan<byte> received = buffer.AsSpan(0, bytesRead); 
                    PrintParsedCommand(received, remoteEndpoint);
                }
            }
            finally
            {
                try
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }

                clientSocket.Close();
                clientSocket.Dispose();

                ArrayPool<byte>.Shared.Return(buffer);

                Console.WriteLine($"{remoteEndpoint} соединение закрыто.");
            }
        }

        private static void PrintParsedCommand(ReadOnlySpan<byte> line, string remoteEndpoint)
        {
            ParsedCommand cmd = CommandParser.Parse(line);

            if (cmd.IsDefault)
            {
                string raw = Encoding.UTF8.GetString(line);
                Console.WriteLine($"{remoteEndpoint} ввел некорректную команду: \"{raw}\"");
                return;
            }

            string command = Encoding.UTF8.GetString(cmd.Command);
            string key = Encoding.UTF8.GetString(cmd.Key);
            string value = cmd.Value.IsEmpty
                ? string.Empty
                : Encoding.UTF8.GetString(cmd.Value);

            Console.WriteLine($"[{remoteEndpoint}] CMD={command} KEY={key} VALUE={value}");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _serverSocket?.Dispose();
        }
    }
}
