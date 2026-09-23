using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Parser.Models;
using Parser.TcpClient.Diagnostics;

namespace Parser.TcpClient;

public sealed class TcpStoreClient : IAsyncDisposable
{
    private readonly Encoding _enc = new UTF8Encoding(false);
    // семафор: только один запрос (WriteAsync + ReadLineAsync) в полёте.
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private string? _host;
    private int _port;

    private System.Net.Sockets.TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(string host, int port)
    {
        _host = host;
        _port = port;

        await ConnectCoreAsync();
    }

    private async Task ConnectCoreAsync()
    {
        DisposeConnection();

        _tcpClient = new System.Net.Sockets.TcpClient { NoDelay = true };
        await _tcpClient.ConnectAsync(_host!, _port);

        _stream = _tcpClient.GetStream();

        _reader = new StreamReader(_stream, _enc, leaveOpen: true);
        _writer = new StreamWriter(_stream, _enc, leaveOpen: true) { AutoFlush = true };
    }

    private void DisposeConnection()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();

        _writer = null;
        _reader = null;
        _stream = null;
        _tcpClient = null;
    }

    public Task<string> SetAsync(string key, UserProfile profile)
    {
        string valueStr = JsonSerializer.Serialize(profile);
        return SendCommandAsync("SET", key, valueStr);
    }

    public Task<string> GetAsync(string key)
    {
        return SendCommandAsync("GET", key, null);
    }

    public Task<string> DeleteAsync(string key)
    {
        return SendCommandAsync("DELETE", key, null);
    }

    private async Task<string> SendCommandAsync(string command, string key, string? value)
    {
        using var activity = ClientTelemetry.ActivitySource.StartActivity(
            $"TcpStoreClient.{command}", ActivityKind.Client);

        activity?.SetTag("command.name", command);
        activity?.SetTag("command.key", key);

        // Клиентский span телеметрии остаётся независимым от серверного: контекст
        // трассировки по TCP не передаётся, сервер ведёт собственный root trace.
        string message = value is null
            ? $"{command} {key}\r\n"
            : $"{command} {key} {value}\r\n";

        var stopwatch = Stopwatch.StartNew();
        bool success = false;
        try
        {
            var reply = await SendAndReadAsync(message);
            success = command switch
            {
                "SET" => reply == "OK",
                "DELETE" => reply == "OK",
                _ => reply is not null && !reply.StartsWith("-ERR", StringComparison.Ordinal)
            };
            activity?.SetTag("status", success ? "ok" : "fail");
            return reply;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("status", "error");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var tags = new KeyValuePair<string, object?>[]
            {
                new("command.name", command),
                new("status", success ? "ok" : "fail")
            };
            ClientTelemetry.RequestsCounter.Add(1, tags);
            ClientTelemetry.RequestDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    private async Task<string> SendAndReadAsync(string message)
    {
        await _gate.WaitAsync();
        try
        {
            try
            {
                await SendAsync(message);
                return await ReadResponseAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                // Соединение разорвано (например, хостом/сетью Docker) — пробуем переподключиться один раз.
                await ConnectCoreAsync();
                await SendAsync(message);
                return await ReadResponseAsync();
            }
        }
        finally { _gate.Release(); }
    }

    private async Task SendAsync(string message)
    {
        if (_writer == null) throw new InvalidOperationException("Клиент не подключен");

        await _writer.WriteAsync(message);
    }

    private async Task<string> ReadResponseAsync()
    {
        if (_reader == null) throw new InvalidOperationException("Клиент не подключен");

        return (await _reader.ReadLineAsync()) ?? "[Соединение закрыто]";
    }

    public ValueTask DisposeAsync()
    {
        DisposeConnection();
        return ValueTask.CompletedTask;
    }
}
