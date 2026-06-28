using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Parser.Parsing;
using Parser.Storage;

namespace Parser.Servers
{
    public class TcpServer : IDisposable
    {
        private Socket? _serverSocket;
        private readonly CancellationTokenSource _cts;
        private readonly SimpleStore _store;

        private const int _BUFFERSIZE = 1024;

        private readonly IPAddress _address;
        private readonly int _port;

        private const string OK = "OK\r\n";
        private const string NIL = "(nil)\r\n";
        private const string ERR = "-ERR Unknown command\r\n";

        public TcpServer(SimpleStore store)
        {
            _store = store;
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

        private async Task ProcessClientAsync(Socket clientSocket, CancellationToken cancellationToken)
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

                    ParsedCommand cmd = CommandParser.Parse(received);
                    byte[] response;

                    if (cmd.IsDefault)
                    {
                        string raw = Encoding.UTF8.GetString(received);
                        Console.WriteLine($"{remoteEndpoint} ввел некорректную команду: \"{raw}\"");
                        response = Encoding.UTF8.GetBytes(ERR);
                    }
                    else
                    {
                        string command = Encoding.UTF8.GetString(cmd.Command).ToUpperInvariant();
                        string key = Encoding.UTF8.GetString(cmd.Key);

                        Console.WriteLine($"[{remoteEndpoint}] CMD={command} KEY={key}");

                        switch (command)
                        {
                            case "SET":
                                _store.Set(key, cmd.Value.ToArray());
                                response = Encoding.UTF8.GetBytes(OK);
                                break;
                            case "GET":
                                byte[]? result = _store.Get(key);
                                if (result is null)
                                {
                                    response = Encoding.UTF8.GetBytes(NIL);
                                }
                                else
                                {
                                    var crlf = Encoding.UTF8.GetBytes("\r\n");
                                    response = new byte[result.Length + crlf.Length];
                                    result.CopyTo(response, 0);
                                    crlf.CopyTo(response, result.Length);
                                }
                                break;
                            case "DELETE":
                                _store.Delete(key);
                                response = Encoding.UTF8.GetBytes(OK);
                                break;
                            default:
                                response = Encoding.UTF8.GetBytes(ERR);
                                break;
                        }
                    }

                    await clientSocket.SendAsync(response, SocketFlags.None, cancellationToken);
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

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _serverSocket?.Dispose();
        }
    }
}
