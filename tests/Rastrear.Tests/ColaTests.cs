using System.Diagnostics;
using Rastrear.Core;
using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public class ColaTests
{
    private static string V(params string[] parts) => Path.Combine([Path.GetTempPath(), "v", .. parts]);

    [Fact]
    public void Cola_progreso_por_archivo_y_global_y_resumen()
    {
        BatchQueue cola = new();
        Assert.Equal(3, cola.Enqueue([V("a.mp4"), V("b.mov"), V("A.MP4"), V("c.mkv"), ""]));
        Assert.Equal(0.0, cola.OverallFraction);
        Assert.Equal(string.Empty, cola.ProgressText);

        BatchItem? a = cola.StartNext();
        Assert.NotNull(a);
        Assert.Equal("a.mp4", a.FileName);
        Assert.Equal(BatchItemState.Running, a.State);
        cola.ReportProgress(0.4, "Siguiendo");
        Assert.Equal("Archivo 1 de 3 · 40 % · Siguiendo", cola.ProgressText);
        Assert.Equal(0.4 / 3, cola.OverallFraction, 6);

        cola.MarkDone(V("a-seguimiento.csv"));
        Assert.Equal("hecho → a-seguimiento.csv", a.StatusText);
        Assert.Equal(1.0 / 3, cola.OverallFraction, 6);

        BatchItem? b = cola.StartNext();
        Assert.NotNull(b);
        cola.MarkFailed("No se encontró el módulo");
        Assert.Equal("error: No se encontró el módulo", b.StatusText);

        BatchItem? c = cola.StartNext();
        Assert.NotNull(c);
        cola.ReportProgress(0.5, "Estabilizando");
        cola.MarkCancelled();
        Assert.Equal("cancelado", c.StatusText);
        Assert.False(cola.HasPending);
        Assert.Null(cola.StartNext());
        Assert.Equal(1.0, cola.OverallFraction);
        Assert.Equal("1 hecho, 1 con error, 1 cancelado", cola.Summary);

        cola.Reset();
        Assert.True(cola.HasPending);
        Assert.Equal("3 pendientes", cola.Summary);
        Assert.Equal(0.0, cola.OverallFraction);

        cola.Clear();
        Assert.Equal(0, cola.Count);
    }

    [Fact]
    public void Cancelar_deja_los_pendientes_para_reanudar_y_un_solo_archivo_no_numera()
    {
        BatchQueue cola = new();
        cola.Enqueue([V("a.mp4"), V("b.mp4")]);
        cola.StartNext();
        cola.MarkCancelled();
        Assert.True(cola.HasPending);
        Assert.Equal("1 cancelado, 1 pendiente", cola.Summary);
        cola.RequeueCancelled();
        Assert.Equal("2 pendientes", cola.Summary);
        Assert.Equal("a.mp4", cola.StartNext()!.FileName); // El cancelado se repite

        BatchQueue una = new();
        una.Enqueue([V("solo.mp4")]);
        una.StartNext();
        una.ReportProgress(0.25, "Quitando fondo");
        Assert.Equal("25 % · Quitando fondo", una.ProgressText);
        Assert.Throws<InvalidOperationException>(una.Clear);
    }

    [Fact]
    public void Al_soltar_solo_entran_los_videos_sin_repetidos()
    {
        List<string> videos = BatchQueue.FilterDroppedPaths([V("a.MP4"), V("notas.txt"), V("b.mkv"), V("a.mp4"), V("foto.png")]);
        Assert.Equal([V("a.MP4"), V("b.mkv")], videos);
    }

    [Fact]
    public void Seguir_necesita_clic_y_no_admite_cola()
    {
        Assert.False(BatchQueue.SupportsTool(0)); // Seguir
        for (int tool = 1; tool < UserPreferences.ToolNames.Length; tool++)
        {
            Assert.True(BatchQueue.SupportsTool(tool));
        }

        // Al soltar tres con «Seguir» solo entra el primero; con cualquier otra, los tres
        BatchQueue cola = new();
        Assert.Equal(1, cola.Enqueue([V("a.mp4"), V("b.mp4"), V("c.mp4")], toolIndex: 0));
        Assert.Equal("a.mp4", cola.Items[0].FileName);
        cola.Clear();
        Assert.Equal(3, cola.Enqueue([V("a.mp4"), V("b.mp4"), V("c.mp4")], toolIndex: 1));
        Assert.Equal(1, cola.Enqueue(["", V("d.mp4")], toolIndex: 0)); // Los vacíos no cuentan como «el primero»
        Assert.Equal(4, cola.Count);

        // Al pasar a «Seguir» con varios en cola queda solo el video cargado (b); con otra herramienta no se toca nada
        Assert.False(cola.TrimToTool(2, V("b.mp4")));
        Assert.Equal(4, cola.Count);
        Assert.True(cola.TrimToTool(0, V("B.MP4")));
        Assert.Single(cola.Items);
        Assert.Equal("b.mp4", cola.Items[0].FileName);
        Assert.False(cola.TrimToTool(0, V("b.mp4"))); // Ya no hay nada que quitar

        // Sin video cargado en la cola se queda el primero; con uno corriendo no se recorta
        cola.Clear();
        cola.Enqueue([V("a.mp4"), V("b.mp4")]);
        cola.StartNext();
        Assert.Throws<InvalidOperationException>(() => cola.TrimToTool(0, null));
        cola.MarkDone(null);
        Assert.True(cola.TrimToTool(0, null));
        Assert.Equal("a.mp4", cola.Items[0].FileName);
        Assert.Null(cola.Current);

        string aviso = BatchQueue.NotQueuedText(3, "a.mp4");
        Assert.Contains("«a.mp4»", aviso);
        Assert.Contains("2 no entraron", aviso);
        Assert.Contains("Seguir", BatchQueue.TrimmedText);
    }

    [Fact]
    public void Nombre_de_salida_junto_a_la_entrada_sin_pisar()
    {
        string entrada = Path.Combine("carpeta", "clip.mp4");
        string dir = Path.GetDirectoryName(Path.GetFullPath(entrada))!;
        Assert.Equal(Path.Combine(dir, "clip-fondo.webm"), OutputNaming.OutputPathFor(entrada, 1, exists: _ => false));
        Assert.Equal(Path.Combine(dir, "clip-mascara.mp4"), OutputNaming.OutputPathFor(entrada, 1, backgroundMask: true, exists: _ => false));
        Assert.Equal(Path.Combine(dir, "clip-seguimiento.csv"), OutputNaming.OutputPathFor(entrada, 0, exists: _ => false));
        Assert.Equal(Path.Combine(dir, "clip-detecciones.json"), OutputNaming.OutputPathFor(entrada, 2, exists: _ => false));
        Assert.Equal(Path.Combine(dir, "clip-reencuadrado.mp4"), OutputNaming.OutputPathFor(entrada, 3, exists: _ => false));

        HashSet<string> ocupados = [Path.Combine(dir, "clip-estabilizado.mp4"), Path.Combine(dir, "clip-estabilizado (2).mp4")];
        Assert.Equal(Path.Combine(dir, "clip-estabilizado (3).mp4"), OutputNaming.OutputPathFor(entrada, 4, exists: ocupados.Contains));
    }

    [Fact]
    public async Task Cancelar_mata_los_procesos_de_su_ejecucion_y_no_los_de_otra()
    {
        // Un solo proceso real, sin hijos (matar el árbol enumera procesos y es lo que tarda): «ping» con espera en
        // Windows, «sleep» en Linux. Lo ajeno es una entrada falsa: fuera de toda ejecución el registro ni la apunta.
        bool ajenoMuerto = false;
        Assert.Null(ChildProcessRegistry.Current);
        using (ChildProcessRegistry.Track("ajeno", () => ajenoMuerto = true, () => ajenoMuerto, _ => Task.FromResult(true)))
        {
            TaskExecutionController controlador = new();
            CancellationToken ct = controlador.BeginExecution();
            Assert.NotNull(controlador.Processes);
            Assert.Same(controlador.Processes, ChildProcessRegistry.Current);

            ProcessStartInfo psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("ping.exe", "-n 31 127.0.0.1")
                : new ProcessStartInfo("sleep", "30");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            using Process propio = Process.Start(psi)!;
            bool moduloMuerto = false;
            try
            {
                ChildProcessRegistry.TrackUntilExit(propio, "ffmpeg (prueba)");
                using (ChildProcessRegistry.Track("módulo (prueba)", () => moduloMuerto = true, () => moduloMuerto, _ => Task.FromResult(moduloMuerto)))
                {
                    Assert.Equal(2, controlador.Processes.LiveCount);

                    Stopwatch sw = Stopwatch.StartNew();
                    bool cancelado = await controlador.CancelAsync(TimeSpan.FromSeconds(5));
                    sw.Stop();

                    Assert.True(cancelado);
                    Assert.True(ct.IsCancellationRequested);
                    Assert.True(propio.HasExited, "ffmpeg (prueba) seguía vivo tras cancelar");
                    Assert.True(moduloMuerto);
                    Assert.Empty(controlador.LeftAliveAfterCancel);
                    Assert.Equal(0, controlador.Processes.LiveCount);
                    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"cancelar tardó {sw.ElapsedMilliseconds} ms");
                    Assert.False(ajenoMuerto, "cancelar mató un proceso de otra ejecución");
                }
            }
            finally
            {
                try { if (!propio.HasExited) propio.Kill(); } catch (InvalidOperationException) { }
            }
        }
    }
}
