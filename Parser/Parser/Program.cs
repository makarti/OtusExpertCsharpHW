using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Parser.Diagnostics;
using Parser.Servers;
using Parser.Storage;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

// Явно закрепляем W3C-формат Activity.Id независимо от порядка инициализации
// TracerProvider, т.к. клиент и сервер должны понимать один и тот же формат traceparent
// для склейки спанов в единый трейс.
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

ResourceBuilder BuildResource() => ResourceBuilder.CreateDefault().AddService(Telemetry.ServiceName);

var tracerProviderBuilder = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(BuildResource())
    .AddSource(Telemetry.ServiceName);

var meterProviderBuilder = OpenTelemetry.Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(BuildResource())
    .AddMeter(Telemetry.ServiceName);

if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    tracerProviderBuilder.AddOtlpExporter();
    meterProviderBuilder.AddOtlpExporter();
}
else
{
    tracerProviderBuilder.AddConsoleExporter();
    meterProviderBuilder.AddConsoleExporter();
}

using var tracerProvider = tracerProviderBuilder.Build();
using var meterProvider = meterProviderBuilder.Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(BuildResource());
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
        options.ParseStateValues = true;

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            options.AddOtlpExporter();
        }
        else
        {
            options.AddConsoleExporter();
        }
    });
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<TcpServer>();

var hostEnv = Environment.GetEnvironmentVariable("PARSER_HOST");
var portEnv = Environment.GetEnvironmentVariable("PARSER_PORT");

var address = string.IsNullOrWhiteSpace(hostEnv) ? IPAddress.Loopback : IPAddress.Parse(hostEnv);
var port = int.TryParse(portEnv, out var parsedPort) ? parsedPort : 8080;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // не завершаем процесс сразу
    Console.WriteLine("\n[Program] Shutting down...");
    cts.Cancel();
};

// docker stop посылает SIGTERM, а не SIGINT — без этого обработчика контейнер
// останавливался бы только по истечении таймаута и получению SIGKILL.
using var sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
{
    ctx.Cancel = true;
    Console.WriteLine("\n[Program] SIGTERM received, shutting down...");
    cts.Cancel();
});

using var store = new SimpleStore();
using var server = new TcpServer(store, address, port, logger);
Task serverTask = server.StartAsync(cts.Token);

// Console.ReadLine() не подходит для контейнеров: без интерактивного stdin он сразу
// возвращает EOF (null), из-за чего процесс мгновенно завершался после старта.
// Ждём отмены токена (Ctrl+C локально или docker stop/SIGTERM в контейнере).
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // ожидаемое завершение по отмене токена
}

if (!cts.IsCancellationRequested)
    cts.Cancel();

try
{
    await serverTask;
}
catch (OperationCanceledException ex)
{
    Console.WriteLine(ex);
}

