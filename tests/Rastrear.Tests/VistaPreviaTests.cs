using Rastrear.Core;
using Rastrear.Core.Model;
using Rastrear.Core.Ui;
using Rastrear.Core.Vision;

namespace Rastrear.Tests;

public class VistaPreviaTests
{
    private static byte[] Cuadro(int w, int h, Func<int, int, (byte B, byte G, byte R)> color)
    {
        byte[] bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte b, byte g, byte r) = color(x, y);
                int o = ((y * w) + x) * 4;
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
            }
        }
        return bgra;
    }

    [Fact]
    public void Recorte_del_reencuadre_copia_los_pixeles_del_rectangulo_centrado()
    {
        // 16×8: la mitad izquierda es roja, la derecha azul; el recorte 1:1 (8×8) centrado toma 4 columnas de cada una.
        byte[] bgra = Cuadro(16, 8, (x, _) => x < 8 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)0, (byte)0));
        PreviewFrame p = FramePreview.CropForAspect(bgra, 16, 8, "1:1");
        Assert.Equal(8, p.Width);
        Assert.Equal(8, p.Height);
        Assert.Equal(8 * 8 * 4, p.Bgra.Length);
        Assert.Equal(255, p.Bgra[2]); // (0,0): rojo (columna 4 del original)
        Assert.Equal(255, p.Bgra[(7 * 4)]); // (7,0): azul (columna 11)
        Assert.Equal(255, p.Bgra[(((7 * 8) + 3) * 4) + 2]); // (3,7): rojo

        // 9:16 sobre 16×8: alto 8, ancho 4,5 → 5 (redondeado) y dentro de la imagen
        PreviewFrame v = FramePreview.CropForAspect(bgra, 16, 8, "9:16");
        Assert.Equal(5, v.Width);
        Assert.Equal(8, v.Height);

        // Un rectángulo que se sale se encaja
        PreviewFrame r = FramePreview.Crop(bgra, 16, 8, new UiRect(14, -3, 10, 20));
        Assert.Equal(10, r.Width);
        Assert.Equal(8, r.Height);
        Assert.Equal(255, r.Bgra[2]); // Empieza en la columna 6: roja
        Assert.Equal(255, r.Bgra[(9 * 4)]); // Acaba en la 15: azul
    }

    [Fact]
    public void Mascara_sobre_tablero_o_en_grises()
    {
        byte[] bgra = Cuadro(4, 2, (_, _) => ((byte)10, (byte)20, (byte)30));
        byte[] mask = [255, 0, 128, 255, 0, 0, 255, 255];

        PreviewFrame alfa = FramePreview.ApplyMask(bgra, mask, 4, 2, asMask: false, checkerSize: 1);
        Assert.Equal([10, 20, 30, 255], alfa.Bgra[0..4]); // Sujeto entero: el color original
        byte tablero = FramePreview.Checker(1, 0, 1);
        Assert.Equal([tablero, tablero, tablero, 255], alfa.Bgra[4..8]); // Fondo entero: el tablero
        byte mitad = (byte)(((30 * 128) + (FramePreview.Checker(2, 0, 1) * 127) + 127) / 255);
        Assert.Equal(mitad, alfa.Bgra[(2 * 4) + 2]); // Mezcla al 50 %

        PreviewFrame gris = FramePreview.ApplyMask(bgra, mask, 4, 2, asMask: true);
        Assert.Equal([255, 255, 255, 255], gris.Bgra[0..4]);
        Assert.Equal([0, 0, 0, 255], gris.Bgra[4..8]);
        Assert.Equal([128, 128, 128, 255], gris.Bgra[8..12]);

        Assert.NotEqual(FramePreview.Checker(0, 0, 16), FramePreview.Checker(16, 0, 16));
        Assert.Equal(FramePreview.Checker(0, 0, 16), FramePreview.Checker(16, 16, 16));

        Assert.True(FramePreview.SupportsPreview(1));
        Assert.True(FramePreview.SupportsPreview(3));
        Assert.False(FramePreview.SupportsPreview(0));
        Assert.Throws<ArgumentException>(() => FramePreview.ApplyMask(bgra, mask, 4, 3, asMask: true));
    }

    /// <summary>Un módulo de mentira: cuenta llamadas, se puede «morir» y sabe si lo cerraron.</summary>
    private sealed class ModuloFalso : IAsyncDisposable
    {
        public int Calls;
        public bool Exited;
        public Exception? ErrorAlCerrar;
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ErrorAlCerrar is { } ex ? ValueTask.FromException(ex) : ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task La_sesion_deja_el_modulo_vivo_entre_cuadros_y_lo_rearranca_si_muere_o_falla()
    {
        List<ModuloFalso> arrancados = [];
        ModuleSession<ModuloFalso> sesion = new(
            _ => { ModuloFalso m = new(); arrancados.Add(m); return Task.FromResult(m); },
            m => m.Exited);
        Assert.False(sesion.IsAlive);
        Assert.Equal(0, sesion.StartCount);

        // Varios cuadros seguidos (y herramientas cambiando por medio): el mismo proceso
        Assert.Equal(1, await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(2, await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(3, await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(1, sesion.StartCount);
        Assert.True(sesion.IsAlive);
        Assert.Single(arrancados);

        // Cancelar una petición (cambió un parámetro antes de acabar) no cierra el módulo
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sesion.RunAsync<int>((_, _) => throw new OperationCanceledException()));
        Assert.True(sesion.IsAlive);
        Assert.False(arrancados[0].Disposed);

        // Un fallo cierra el módulo y la siguiente llamada arranca otro
        await Assert.ThrowsAsync<InvalidOperationException>(() => sesion.RunAsync<int>((_, _) => throw new InvalidOperationException("se rompió")));
        Assert.True(arrancados[0].Disposed);
        Assert.False(sesion.IsAlive);
        Assert.Equal(1, await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(2, sesion.StartCount);

        // El proceso muere por su cuenta: se nota y se arranca otro
        arrancados[1].Exited = true;
        Assert.False(sesion.IsAlive);
        await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls));
        Assert.Equal(3, sesion.StartCount);
        Assert.True(arrancados[1].Disposed);

        // Dos peticiones a la vez van de una en una sobre el mismo módulo
        TaskCompletionSource primera = new();
        Task<int> a = sesion.RunAsync(async (m, _) => { await primera.Task; return ++m.Calls; });
        Task<int> b = sesion.RunAsync((m, _) => Task.FromResult(++m.Calls));
        await Task.Delay(50);
        Assert.False(b.IsCompleted);
        primera.SetResult();
        Assert.Equal(2, await a);
        Assert.Equal(3, await b);
        Assert.Equal(3, sesion.StartCount);

        // Cerrar sin desechar (soltar «Después») cierra el módulo; la sesión arrancaría otro
        await sesion.CloseAsync();
        Assert.True(arrancados[2].Disposed);
        Assert.False(sesion.IsAlive);

        // Desechar cierra y no sirve más
        await sesion.RunAsync((m, _) => Task.FromResult(0));
        await sesion.DisposeAsync();
        Assert.True(arrancados[3].Disposed);
        Assert.False(sesion.IsAlive);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sesion.RunAsync((_, _) => Task.FromResult(0)));
        await sesion.DisposeAsync(); // Dos veces no pasa nada
    }

    [Fact]
    public async Task La_vista_previa_del_fondo_comprueba_el_cuadro_antes_de_tocar_el_modulo()
    {
        // Un cuadro que no cuadra con su tamaño se rechaza sin buscar ni arrancar el módulo de visión
        await using ModuleSession<Rodaje.Modules.Protocol.ModuleProcess> sesion = VisionService.CreateSession();
        await Assert.ThrowsAsync<ArgumentException>(() => VisionService.MatteFrameAsync(sesion, new byte[3], 4, 2));
        await Assert.ThrowsAsync<ArgumentException>(() => VisionService.MatteFrameAsync(sesion, new byte[32], 0, 2));
        await Assert.ThrowsAsync<ArgumentNullException>(() => VisionService.MatteFrameAsync(null!, new byte[32], 4, 2));
        Assert.Equal(0, sesion.StartCount);
        Assert.False(sesion.IsAlive);
    }

    [Fact]
    public async Task DisposeAsync_mientras_hay_llamada_en_vuelo_no_rompe_la_llamada_siguiente()
    {
        List<ModuloFalso> arrancados = [];
        ModuleSession<ModuloFalso> sesion = new(
            _ => { ModuloFalso m = new(); arrancados.Add(m); return Task.FromResult(m); },
            m => m.Exited);

        TaskCompletionSource llamadaEnVueloEntro = new();
        TaskCompletionSource llamadaEnVueloContinua = new();

        // Lanzar una llamada que se queda a la mitad
        Task<int> enVuelo = sesion.RunAsync(async (m, _) =>
        {
            m.Calls++;
            llamadaEnVueloEntro.SetResult();
            await llamadaEnVueloContinua.Task;
            return m.Calls;
        });

        await llamadaEnVueloEntro.Task;
        Assert.Single(arrancados);
        Assert.False(arrancados[0].Disposed);

        // Concurrentemente se desecha la sesión (el módulo en vuelo aún tiene el lock del semáforo)
        Task dispose = sesion.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);

        // Permitir que la llamada en vuelo termine
        llamadaEnVueloContinua.SetResult();
        Assert.Equal(1, await enVuelo);

        // Ahora DisposeAsync debe completar limpiamente
        await dispose;
        Assert.True(arrancados[0].Disposed);
        Assert.False(sesion.IsAlive);

        // La llamada siguiente sobre esta sesión desechada lanza ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sesion.RunAsync((_, _) => Task.FromResult(0)));

        // Y una sesión nueva posterior arranca un módulo nuevo sin errores
        ModuleSession<ModuloFalso> nuevaSesion = new(
            _ => { ModuloFalso m = new(); arrancados.Add(m); return Task.FromResult(m); },
            m => m.Exited);
        Assert.Equal(1, await nuevaSesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(2, arrancados.Count);
        Assert.True(nuevaSesion.IsAlive);
        await nuevaSesion.DisposeAsync();
    }

    [Fact]
    public async Task El_arranque_que_falla_no_deja_nada_y_la_siguiente_llamada_lo_reintenta()
    {
        // Un módulo que no responde al arrancar: la excepción sale tal cual, no queda medio módulo
        // colgado y la sesión no queda envenenada (la puerta se suelta y se reintenta limpio).
        int intentos = 0;
        List<ModuloFalso> arrancados = [];
        ModuleSession<ModuloFalso> sesion = new(
            _ =>
            {
                intentos++;
                if (intentos == 1) throw new RastrearModuleException("El módulo de visión no responde");
                ModuloFalso m = new();
                arrancados.Add(m);
                return Task.FromResult(m);
            },
            m => m.Exited);

        var ex = await Assert.ThrowsAsync<RastrearModuleException>(() => sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal("El módulo de visión no responde", ex.Message);
        Assert.Equal(0, sesion.StartCount);   // solo cuentan los arranques que llegaron a buen puerto
        Assert.False(sesion.IsAlive);
        Assert.Empty(arrancados);             // nada que cerrar: no se colgó ningún proceso

        Assert.Equal(1, await sesion.RunAsync((m, _) => Task.FromResult(++m.Calls)));
        Assert.Equal(1, sesion.StartCount);
        Assert.True(sesion.IsAlive);
        await sesion.DisposeAsync();
    }

    [Fact]
    public async Task La_llamada_cancelada_esperando_en_la_puerta_no_toca_el_modulo()
    {
        List<ModuloFalso> arrancados = [];
        ModuleSession<ModuloFalso> sesion = new(
            _ => { ModuloFalso m = new(); arrancados.Add(m); return Task.FromResult(m); },
            m => m.Exited);

        // La primera llamada ocupa la puerta un rato
        TaskCompletionSource dentro = new();
        TaskCompletionSource suelta = new();
        Task<int> primera = sesion.RunAsync(async (m, _) =>
        {
            m.Calls++;
            dentro.SetResult();
            await suelta.Task;
            return m.Calls;
        });
        await dentro.Task;

        // La segunda llega con la cancelación ya pedida: espera en la puerta y salta sin tocar el módulo
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sesion.RunAsync((m, _) => Task.FromResult(++m.Calls), cts.Token));
        Assert.Equal(1, arrancados[0].Calls);      // ni siquiera entró
        Assert.False(arrancados[0].Disposed);
        Assert.True(sesion.IsAlive);
        Assert.Equal(1, sesion.StartCount);        // no arrancó otro

        suelta.SetResult();
        Assert.Equal(1, await primera);
        await sesion.DisposeAsync();
    }

    [Fact]
    public async Task Un_modulo_que_no_cierra_no_se_lleva_el_error_de_la_llamada()
    {
        // La llamada falla y además el módulo no responde al cerrarlo: sale el error de la llamada
        // (el de cierre se traga, como al apagar un proceso ya muerto) y la sesión puede seguir.
        ModuleSession<ModuloFalso> sesion = new(
            _ => Task.FromResult(new ModuloFalso { ErrorAlCerrar = new IOException("pipe roto") }),
            m => m.Exited);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sesion.RunAsync<int>((_, _) => throw new InvalidOperationException("el módulo devolvió basura")));
        Assert.Equal("el módulo devolvió basura", ex.Message);
        Assert.False(sesion.IsAlive);

        // Cerrar a mano con un módulo muerto tampoco lanza
        await sesion.RunAsync((m, _) => Task.FromResult(0));
        await sesion.CloseAsync();
        Assert.False(sesion.IsAlive);
        await sesion.DisposeAsync();
    }
}
