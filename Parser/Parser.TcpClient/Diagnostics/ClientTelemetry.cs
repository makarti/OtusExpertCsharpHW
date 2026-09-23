using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Parser.TcpClient.Diagnostics;

/// <summary>
/// Централизованное хранилище источников трассировки и метрик клиента TcpStoreClient.
/// </summary>
public static class ClientTelemetry
{
    public const string ServiceName = "Parser.TcpClient";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> RequestsCounter =
        Meter.CreateCounter<long>("parser.client.requests", description: "Количество отправленных клиентом запросов");

    public static readonly Histogram<double> RequestDurationHistogram =
        Meter.CreateHistogram<double>("parser.client.request.duration", unit: "ms", description: "Длительность запроса клиента в миллисекундах");
}
