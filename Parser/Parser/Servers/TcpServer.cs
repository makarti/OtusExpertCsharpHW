using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Parser.Models;
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


        private readonly byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
        private readonly byte[] OK = Encoding.UTF8.GetBytes("OK\r\n");
        private readonly byte[] NIL = Encoding.UTF8.GetBytes("(nil)\r\n");
        private readonly byte[] ERR = Encoding.UTF8.GetBytes("-ERR Unknown command\r\n");

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

                    var responses = ProcessBuffer(buffer, bytesRead);

                    for(int i = 0; i < responses.Count; i++)
                    {
                        await clientSocket.SendAsync(responses[i], SocketFlags.None, cancellationToken);
                    }
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

        private List<byte[]> ProcessBuffer(byte[] buffer, int bytesRead)
        {

            ReadOnlySpan<byte> received = buffer.AsSpan(0, bytesRead);
            var responses = new List<byte[]>();
            var processed = 0;

            while (processed < received.Length)
            {
                var remaining = received.Slice(processed);
                var newlineIndex = remaining.IndexOf(crlf);

                if (newlineIndex == -1)
                    break;

                var commandRow = remaining.Slice(0, newlineIndex);
                processed += newlineIndex + 2;

                responses.Add(ProcessLine(commandRow));
            }

            return responses;
        }

        private byte[] ProcessLine(ReadOnlySpan<byte> received)
        {
            ParsedCommand cmd = CommandParser.Parse(received);
            byte[] response;


            if (cmd.IsDefault)
            {
                response = ERR;
            }
            else
            {
                string command = Encoding.UTF8.GetString(cmd.Command).ToUpperInvariant();
                string key = Encoding.UTF8.GetString(cmd.Key);

                switch (command)
                {
                    case "SET":
                    {
                        try
                        {
                            var profile = JsonSerializer.Deserialize<UserProfile>(cmd.Value);
                            if (profile != null)
                            {
                                _store.Set(key, profile);
                                response = OK;
                            }
                            else
                            {
                                response = ERR;
                            }
                        }
                        catch
                        {
                            response = ERR;
                        }

                        break;
                    }
                    case "GET":
                        {
                            var profile = _store.Get(key);
                            if (profile != null)
                            {
                                byte[] result = JsonSerializer.SerializeToUtf8Bytes(profile);
                                response = new byte[result.Length + crlf.Length];
                                result.CopyTo(response, 0);
                                crlf.CopyTo(response, result.Length);

                            }
                            else
                            {
                                response = NIL;
                            }

                            break;
                        }
                    case "DELETE":
                        _store.Delete(key);
                        response = OK;
                        break;
                    default:
                        response = ERR;
                        break;
                }
            }
            return response;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _serverSocket?.Dispose();
        }
    }
}
