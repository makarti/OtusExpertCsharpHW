using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Parser.Diagnostics;

/// <summary>
/// Централизованное хранилище источников трассировки и метрик приложения.
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "Parser.TcpServer";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> CommandsProcessedCounter =
        Meter.CreateCounter<long>("parser.commands.processed", description: "Количество обработанных команд");

    public static readonly Histogram<double> CommandDurationHistogram =
        Meter.CreateHistogram<double>("parser.command.duration", unit: "ms", description: "Длительность обработки команды в миллисекундах");
}
