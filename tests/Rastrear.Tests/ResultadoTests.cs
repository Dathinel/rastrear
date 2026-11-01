using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public sealed class ResultadoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "rastrear-resultado-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Abrir_carpeta_selecciona_el_archivo_o_abre_la_carpeta_si_ya_no_esta()
    {
        Directory.CreateDirectory(_raiz);
        string archivo = Path.Combine(_raiz, "clip-fondo.webm");
        File.WriteAllText(archivo, "");
        List<(string Exe, string Args)> lanzados = [];

        Assert.True(ResultActions.OpenFolder(archivo, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(("explorer.exe", $"/select,\"{archivo}\""), lanzados[0]);
        Assert.Equal(archivo, ResultActions.ClipboardText(archivo));
        Assert.Equal(_raiz, ResultActions.FolderOf(archivo));

        File.Delete(archivo);
        Assert.True(ResultActions.OpenFolder(archivo, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(("explorer.exe", $"\"{_raiz}\""), lanzados[1]);

        Assert.False(ResultActions.OpenFolder(Path.Combine(_raiz, "no", "existe.mp4"), (exe, args) => lanzados.Add((exe, args))));
        Assert.False(ResultActions.OpenFolder(null, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(2, lanzados.Count);
        Assert.Null(ResultActions.FolderOf(Path.Combine(_raiz, "nada.mp4")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }
}
