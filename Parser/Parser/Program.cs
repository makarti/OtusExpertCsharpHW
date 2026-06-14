using Parser;
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // не завершаем процесс сразу
    Console.WriteLine("\n[Program] Shutting down...");
    cts.Cancel();
};

using var server = new TcpServer();
Task serverTask = server.StartAsync(cts.Token);

Console.ReadLine();

if (!cts.IsCancellationRequested)
    cts.Cancel();

try
{
    await serverTask;
}
catch (OperationCanceledException)
{
}
