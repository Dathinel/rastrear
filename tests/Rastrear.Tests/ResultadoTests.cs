using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public sealed class ResultadoTests : TemporaryFolderTest
{
    [Fact]
    public void Abrir_carpeta_selecciona_el_archivo_o_abre_la_carpeta_si_ya_no_esta()
    {
        string archivo = Touch("clip-fondo.webm");
        List<(string Exe, string Args)> lanzados = [];

        Assert.True(ResultActions.OpenFolder(archivo, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(("explorer.exe", $"/select,\"{archivo}\""), lanzados[0]);
        Assert.Equal(archivo, ResultActions.ClipboardText(archivo));
        Assert.Equal(Root, ResultActions.FolderOf(archivo));

        File.Delete(archivo);
        Assert.True(ResultActions.OpenFolder(archivo, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(("explorer.exe", $"\"{Root}\""), lanzados[1]);

        Assert.False(ResultActions.OpenFolder(PathOf("no", "existe.mp4"), (exe, args) => lanzados.Add((exe, args))));
        Assert.False(ResultActions.OpenFolder(null, (exe, args) => lanzados.Add((exe, args))));
        Assert.Equal(2, lanzados.Count);
        Assert.Null(ResultActions.FolderOf(PathOf("nada.mp4")));
    }
}
