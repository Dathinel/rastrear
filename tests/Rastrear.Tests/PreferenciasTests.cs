using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public sealed class PreferenciasTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "rastrear-prefs-" + Guid.NewGuid().ToString("N"));
    private string Ruta => Path.Combine(_raiz, "preferencias.json");

    [Fact]
    public void Guarda_y_recupera_carpeta_herramienta_y_parametros()
    {
        Assert.Equal(0, UserPreferences.Load(Ruta).ToolIndex); // Sin archivo: por defecto

        Directory.CreateDirectory(_raiz);
        string video = Path.Combine(_raiz, "clip.mp4");
        UserPreferences prefs = new() { ToolIndex = 3, ReframeAspect = "1:1", ReframeSmoothing = 0.7, FollowPatternSize = 61, BackgroundMask = true, DetectTarget = "personas", StabilizeAutoCrop = false };
        prefs.RememberFile(video);
        prefs.Save(Ruta);
        Assert.False(File.Exists(Ruta + ".tmp")); // Escritura atómica: temporal + Move

        UserPreferences leidas = UserPreferences.Load(Ruta);
        Assert.Equal(_raiz, leidas.LastFolder);
        Assert.Equal(_raiz, leidas.LastFolderIfExists);
        Assert.Equal("reencuadrar", leidas.Tool);
        Assert.Equal(3, leidas.ToolIndex);
        Assert.Equal("1:1", leidas.ReframeAspect);
        Assert.Equal(0.7, leidas.ReframeSmoothing);
        Assert.Equal(61, leidas.FollowPatternSize);
        Assert.True(leidas.BackgroundMask);
        Assert.Equal("personas", leidas.DetectTarget);
        Assert.False(leidas.StabilizeAutoCrop);
    }

    [Fact]
    public void Archivo_roto_o_valores_fuera_de_rango_vuelven_a_lo_valido()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(Ruta, "{basura");
        Assert.Equal("seguir", UserPreferences.Load(Ruta).Tool);

        File.WriteAllText(Ruta, """{"herramienta":"volar","seguirPatron":999,"reencuadreSuavizado":5,"estabilizarIntensidad":-1,"fondoDispositivo":"tpu","reencuadreAspecto":"3:2","ultimaCarpeta":"Z:\\no-existe"}""");
        UserPreferences p = UserPreferences.Load(Ruta);
        Assert.Equal("seguir", p.Tool);
        Assert.Equal(81, p.FollowPatternSize);
        Assert.Equal(0.9, p.ReframeSmoothing);
        Assert.Equal(0.1, p.StabilizeIntensity);
        Assert.Equal("auto", p.BackgroundDevice);
        Assert.Equal("9:16", p.ReframeAspect);
        Assert.Null(p.LastFolderIfExists);

        // El índice se recorta al selector
        p.ToolIndex = 99;
        Assert.Equal("estabilizar", p.Tool);
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }
}
