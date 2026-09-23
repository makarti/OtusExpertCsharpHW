using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Parser.Diagnostics;
using Parser.Models;
using Parser.Parsing;
using Parser.Storage;

namespace Parser.Servers;

public sealed class TcpServer : IDisposable
{
    private Socket? _serverSocket;
    private readonly CancellationTokenSource _cts;
    private readonly SimpleStore _store;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly ILogger<TcpServer> _logger;

    private const int RecvBufferSize = 4096;

    // Максимальный размер одной команды (защита от истощения памяти).
    private const int MaxMessageSize = 4096;

    // Максимальное число одновременно обслуживаемых подключений.
    private const int MaxConcurrentConnections = 100;

    private readonly IPAddress _address;
    private readonly int _port;

    private static readonly byte[] ResponseOk = Encoding.UTF8.GetBytes("OK\r\n");
    private static readonly byte[] ResponseNil = Encoding.UTF8.GetBytes("(nil)\r\n");
    private static readonly byte[] ResponseErr = Encoding.UTF8.GetBytes("-ERR Unknown command\r\n");
    private static readonly byte[] ResponseErrJson = Encoding.UTF8.GetBytes("-ERR Invalid JSON\r\n");
    private static readonly byte[] ResponseErrTooLarge = Encoding.UTF8.GetBytes("-ERR Message too large\r\n");
    private static readonly byte[] CrLf = Encoding.UTF8.GetBytes("\r\n");

    public TcpServer(SimpleStore store, IPAddress? address = null, int port = 8080, ILogger<TcpServer>? logger = null)
    {
        _store = store;
        _address = address ?? IPAddress.Loopback;
        _port = port;
        _cts = new CancellationTokenSource();
        _connectionSemaphore = new SemaphoreSlim(MaxConcurrentConnections, MaxConcurrentConnections);
        _logger = logger ?? NullLogger<TcpServer>.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var endpoint = new IPEndPoint(_address, _port);
        _serverSocket.Bind(endpoint);
        _serverSocket.Listen();

        _logger.LogInformation("TcpServer запущен на {Address}:{Port}", _address, _port);

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
                _logger.LogError(ex, "Ошибка при принятии подключения");
                continue;
            }

            _logger.LogInformation("Клиент подключен: {RemoteEndPoint}", clientSocket.RemoteEndPoint);

            try
            {
                await _connectionSemaphore.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                clientSocket.Dispose();
                break;
            }

            _ = ProcessClientAsync(clientSocket, cancellationToken);
        }

        _logger.LogInformation("TcpServer остановлен");
    }

    private async Task ProcessClientAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        var remoteEndpoint = clientSocket.RemoteEndPoint?.ToString();

        var stream = new NetworkStream(clientSocket, ownsSocket: false);
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: RecvBufferSize));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result;
                try
                {
                    result = await reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                ReadOnlySequence<byte> buffer = result.Buffer;

                // Извлекаем все полные строки (до \r\n), которые уже накопились
                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    byte[] response = ProcessLine(line);
                    await stream.WriteAsync(response, cancellationToken);
                }

                // Защита от истощения памяти: если незавершённая команда уже превысила лимит,
                // разрываем соединение, не пытаясь обработать сообщение.
                if (!result.IsCompleted && buffer.Length > MaxMessageSize)
                {
                    _logger.LogWarning("{RemoteEndpoint} превысил максимальный размер сообщения ({BufferLength} байт). Соединение будет разорвано.", remoteEndpoint, buffer.Length);
                    await stream.WriteAsync(ResponseErrTooLarge, cancellationToken);
                    break;
                }

                // Говорим PipeReader: "buffer.Start..buffer.End ещё не обработаны,
                // но то что было до buffer.Start — можно освободить".
                reader.AdvanceTo(buffer.Start, buffer.End);

                // Клиент закрыл соединение
                if (result.IsCompleted)
                {
                    _logger.LogInformation("{RemoteEndpoint} отсоединен.", remoteEndpoint);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке клиента {RemoteEndpoint}", remoteEndpoint);
        }
        finally
        {
            await reader.CompleteAsync();
            await stream.DisposeAsync();

            try { clientSocket.Shutdown(SocketShutdown.Both); } catch { }
            clientSocket.Close();
            clientSocket.Dispose();

            _connectionSemaphore.Release();

            _logger.LogInformation("{RemoteEndpoint} соединение закрыто.", remoteEndpoint);
        }
    }

    /// <summary>
    /// Ищет разделитель \r\n в последовательности и, если найден,
    /// возвращает строку перед ним и сдвигает <paramref name="buffer"/> за него.
    /// </summary>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var seqReader = new SequenceReader<byte>(buffer);

        // TryReadTo ищет "\r\n" даже если он "размазан" по границе двух сегментов Pipe
        if (!seqReader.TryReadTo(out line, CrLf, advancePastDelimiter: true))
        {
            line = default;
            return false;
        }

        buffer = buffer.Slice(seqReader.Position);
        return true;
    }


    private byte[] ProcessLine(ReadOnlySequence<byte> lineSeq)
    {
        // строка целиком лежит в одном сегменте Pipe
        if (lineSeq.IsSingleSegment)
            return ProcessLineCore(lineSeq.FirstSpan);

        // команда пришла на стыке двух чтений сокета и оказалась
        // растянута по двум сегментам. Копируем в один буфер из пула.
        byte[] rented = ArrayPool<byte>.Shared.Rent((int)lineSeq.Length);
        try
        {
            lineSeq.CopyTo(rented);
            return ProcessLineCore(rented.AsSpan(0, (int)lineSeq.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private byte[] ProcessLineCore(ReadOnlySpan<byte> received)
    {
        ParsedCommand cmd = CommandParser.Parse(received);

        if (cmd.IsDefault)
            return ResponseErr;

        string command = Encoding.UTF8.GetString(cmd.Command).ToUpperInvariant();
        string key = Encoding.UTF8.GetString(cmd.Key);

        // Сервер ведёт собственный независимый trace: контекст клиента по TCP не передаётся.
        using var activity = Telemetry.ActivitySource.StartActivity("ProcessCommand");

        activity?.SetTag("command.name", command);
        activity?.SetTag("command.key", key);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            switch (command)
            {
                case "SET":
                    {
                        if (cmd.Value.IsEmpty)
                            return ResponseErr;

                        try
                        {
                            var profile = JsonSerializer.Deserialize<UserProfile>(cmd.Value);
                            if (profile is null)
                                return ResponseErrJson;

                            _store.Set(key, profile);
                            return ResponseOk;
                        }
                        catch (JsonException)
                        {
                            return ResponseErrJson;
                        }
                    }

                case "GET":
                    {
                        var profile = _store.Get(key);
                        if (profile is null)
                            return ResponseNil;

                    using var memoryStream = new MemoryStream();
                    profile.SerializeToBinary(memoryStream);
                    byte[] json = memoryStream.ToArray();

                    byte[] response = new byte[json.Length + CrLf.Length];
                    json.CopyTo(response, 0);
                    CrLf.CopyTo(response, json.Length);
                    return response;
                }

                case "DELETE":
                    _store.Delete(key);
                    return ResponseOk;

                default:
                    return ResponseErr;
            }
        }
        finally
        {
            stopwatch.Stop();
            Telemetry.CommandsProcessedCounter.Add(1, new KeyValuePair<string, object?>("command.name", command));
            Telemetry.CommandDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("command.name", command));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _serverSocket?.Dispose();
    }
}
