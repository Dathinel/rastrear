using System.Diagnostics;
using Rastrear.Core.Ui;
using Rastrear.Core.Video;

namespace Rastrear.Tests;

public class ColaTests
{
    [Fact]
    public void Cola_progreso_por_archivo_y_global_y_resumen()
    {
        BatchQueue cola = new();
        string v = Path.Combine(Path.GetTempPath(), "v");
        string V(string nombre) => Path.Combine(v, nombre);
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
        cola.Enqueue([@"C:\v\a.mp4", @"C:\v\b.mp4"]);
        cola.StartNext();
        cola.MarkCancelled();
        Assert.True(cola.HasPending);
        Assert.Equal("1 cancelado, 1 pendiente", cola.Summary);

        BatchQueue una = new();
        una.Enqueue([@"C:\v\solo.mp4"]);
        una.StartNext();
        una.ReportProgress(0.25, "Quitando fondo");
        Assert.Equal("25 % · Quitando fondo", una.ProgressText);
        Assert.Throws<InvalidOperationException>(una.Clear);
    }

    [Fact]
    public void Al_soltar_solo_entran_los_videos_sin_repetidos()
    {
        List<string> videos = BatchQueue.FilterDroppedPaths([@"C:\v\a.MP4", @"C:\v\notas.txt", @"C:\v\b.mkv", @"C:\v\a.mp4", @"C:\v\foto.png"]);
        Assert.Equal([@"C:\v\a.MP4", @"C:\v\b.mkv"], videos);
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
        Assert.Equal(Path.Combine(dir, "clip-reencuadre.mp4"), OutputNaming.OutputPathFor(entrada, 3, exists: _ => false));

        HashSet<string> ocupados = [Path.Combine(dir, "clip-estabilizado.mp4"), Path.Combine(dir, "clip-estabilizado (2).mp4")];
        Assert.Equal(Path.Combine(dir, "clip-estabilizado (3).mp4"), OutputNaming.OutputPathFor(entrada, 4, exists: ocupados.Contains));
    }

    [Fact]
    public async Task Cancelar_mata_los_procesos_apuntados_y_no_deja_ninguno_vivo()
    {
        // Un proceso que dormiría 30 s: en Windows «ping» con espera, en Linux «sleep».
        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 31 127.0.0.1 >nul")
            : new ProcessStartInfo("sleep", "30");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using Process dormido = Process.Start(psi)!;

        bool killed = false;
        using (ChildProcessRegistry.Track(dormido, "ffmpeg (prueba)"))
        using (ChildProcessRegistry.Track("módulo (prueba)", () => killed = true, () => killed, _ => Task.FromResult(killed)))
        {
            Assert.Equal(2, ChildProcessRegistry.LiveCount);

            TaskExecutionController controlador = new();
            CancellationToken ct = controlador.BeginExecution();
            Stopwatch sw = Stopwatch.StartNew();
            bool cancelado = await controlador.CancelAsync(TimeSpan.FromSeconds(10));
            sw.Stop();

            Assert.True(cancelado);
            Assert.True(ct.IsCancellationRequested);
            Assert.True(dormido.HasExited, "ffmpeg (prueba) seguía vivo tras cancelar");
            Assert.True(killed);
            Assert.Empty(controlador.LeftAliveAfterCancel);
            Assert.Equal(0, ChildProcessRegistry.LiveCount);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        }
    }
}
