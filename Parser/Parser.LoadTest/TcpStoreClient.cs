using System.Net.Sockets;
using System.Text;

namespace Parser.LoadTest;
    
public sealed class TcpStoreClient : IAsyncDisposable
{
    private readonly Encoding _enc = new UTF8Encoding(false);

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

    public async Task<string> SetAsync(string key, byte[] value)
    {
        string valueStr = _enc.GetString(value);
        await SendAsync($"SET {key} {valueStr}\r\n");
        return await ReadResponseAsync();
    }

    public async Task<string?> GetAsync(string key)
    {
        await SendAsync($"GET {key}\r\n");
        return await ReadResponseAsync();
    }

    public async Task<string> DeleteAsync(string key)
    {
        await SendAsync($"DELETE {key}\r\n");
        return await ReadResponseAsync();
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
