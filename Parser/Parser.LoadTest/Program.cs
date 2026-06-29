using NBomber.Contracts.Stats;
using NBomber.CSharp;
using Parser.LoadTest;

const string host = "127.0.0.1";
const int port = 8080;

await using var client = new TcpStoreClient();
await client.ConnectAsync(host, port);

var setScenario = Scenario.Create("SetScenario", async context =>
{

    var key   = $"user:data{context.InvocationNumber}";
    var value = System.Text.Encoding.UTF8.GetBytes($"value{context.InvocationNumber}");

    try
    {
        var reply = await client.SetAsync(key, value);
        return reply == "OK"
            ? Response.Ok(statusCode: "OK")
            : Response.Fail(statusCode: reply);
    }
    catch
    {
        return Response.Fail(statusCode: "Ошибка отправки");
    }
})
.WithWarmUpDuration(TimeSpan.FromSeconds(5))
.WithLoadSimulations(
    Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);

NBomberRunner
    .RegisterScenarios(setScenario)
    .WithReportFormats(ReportFormat.Html, ReportFormat.Md)
    .Run();
