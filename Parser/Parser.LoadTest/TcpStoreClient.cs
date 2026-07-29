using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Parser.Models;

namespace Parser.LoadTest;
    
public sealed class TcpStoreClient : IAsyncDisposable
{
    private readonly Encoding _enc = new UTF8Encoding(false);
    // семафор: только один запрос (WriteAsync + ReadLineAsync) в полёте.
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(string host, int port)
    {
        _tcpClient = new TcpClient { NoDelay = true };
        await _tcpClient.ConnectAsync(host, port);

        _stream = _tcpClient.GetStream();

        _reader = new StreamReader(_stream, _enc, leaveOpen: true);
        _writer = new StreamWriter(_stream, _enc, leaveOpen: true) { AutoFlush = true };
    }

    public async Task<string> SetAsync(string key, UserProfile profile)
    {
        string valueStr = JsonSerializer.Serialize(profile);
        return await SendAndReadAsync($"SET {key} {valueStr}\r\n");
    }

    public async Task<string?> GetAsync(string key)
    {
        return await SendAndReadAsync($"GET {key}\r\n");
    }

    public async Task<string> DeleteAsync(string key)
    {
        return await SendAndReadAsync($"DELETE {key}\r\n");
    }

    private async Task<string> SendAndReadAsync(string message)
    {
        await _gate.WaitAsync();
        try
        {
            await SendAsync(message);
            return await ReadResponseAsync();
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

    public async ValueTask DisposeAsync()
    {
        if (_writer != null) await _writer.DisposeAsync();
        _reader?.Dispose();
        if (_stream != null) await _stream.DisposeAsync();
        _tcpClient?.Dispose();
    }
}
