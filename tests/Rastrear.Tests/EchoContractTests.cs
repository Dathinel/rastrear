using System.Diagnostics;
using System.Text.Json;
using Rodaje.Modules.Protocol;
using Xunit.Abstractions;

namespace Rastrear.Tests;

public sealed class EchoContractTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Echo_call_succeeds_and_reports_progress()
    {
        string echoExe = TestMediaHelper.FindEchoModule();
        await using ModuleProcess module = await ModuleProcess.StartAsync(echoExe);
        Assert.Equal("rodaje-eco", module.Client.Module.Id);

        List<ModuleProgress> progressReports = [];
        Progress<ModuleProgress> progress = new(p =>
        {
            lock (progressReports)
            {
                progressReports.Add(p);
            }
        });

        using JsonDocument doc = JsonDocument.Parse("""{"pasos":10,"msPorPaso":10}""");
        ModuleResult result = await module.Client.InvokeAsync("contar", doc.RootElement.Clone(), progress: progress);

        Assert.NotNull(result.Value);
        Assert.Equal(10, result.Value.Value.GetProperty("pasos").GetInt32());

        lock (progressReports)
        {
            Assert.NotEmpty(progressReports);
            Assert.Equal(1.0, progressReports[^1].Fraction);
        }
    }

    [Fact]
    public async Task Cancellation_takes_less_than_one_second()
    {
        string echoExe = TestMediaHelper.FindEchoModule();
        await using ModuleProcess module = await ModuleProcess.StartAsync(echoExe);

        using CancellationTokenSource cts = new();
        using JsonDocument doc = JsonDocument.Parse("""{"pasos":10000,"msPorPaso":10}""");

        Task call = module.Client.InvokeAsync("contar", doc.RootElement.Clone(), cancellationToken: cts.Token);
        await Task.Delay(50);

        Stopwatch watch = Stopwatch.StartNew();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        watch.Stop();

        output.WriteLine($"Tiempo de confirmación de cancelación: {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"La cancelación tardó {watch.ElapsedMilliseconds} ms (límite: 1 s).");
        Assert.False(module.Exited.IsCompleted, "El módulo debe seguir vivo tras la cancelación.");

        // Verificar que sigue respondiendo
        await module.Client.PingAsync();
    }

    [Fact]
    public async Task Module_that_dies_mid_call_fails_fast_with_clear_error()
    {
        string echoExe = TestMediaHelper.FindEchoModule();
        await using ModuleProcess module = await ModuleProcess.StartAsync(echoExe);

        using JsonDocument doc = JsonDocument.Parse("""{"codigo":3}""");
        Stopwatch watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<ModuleDisconnectedException>(() =>
            module.Client.InvokeAsync("morir", doc.RootElement.Clone()));

        watch.Stop();
        output.WriteLine($"Tiempo de detección de muerte del módulo: {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"El fallo tardó {watch.ElapsedMilliseconds} ms (límite: 1 s).");

        int exitCode = await module.Exited.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(3, exitCode);
    }
}
