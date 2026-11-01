using Rastrear.Core;
using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public sealed class PreferenciasTests : TemporaryFolderTest
{
    private string Ruta => PathOf("preferencias.json");

    [Fact]
    public void Guarda_y_recupera_carpeta_herramienta_y_parametros()
    {
        Assert.Equal(0, UserPreferences.Load(Ruta).ToolIndex); // Sin archivo: por defecto

        string video = PathOf("clip.mp4");
        UserPreferences prefs = new() { ToolIndex = 3, ReframeAspect = "1:1", ReframeSmoothing = 0.7, FollowPatternSize = 61, BackgroundMask = true, DetectTarget = "personas", StabilizeAutoCrop = false };
        prefs.RememberFile(video);
        prefs.Save(Ruta);
        Assert.False(File.Exists(Ruta + AtomicFile.TemporarySuffix)); // Escritura atómica: temporal + Move

        UserPreferences leidas = UserPreferences.Load(Ruta);
        Assert.Equal(Root, leidas.LastFolder);
        Assert.Equal(Root, leidas.LastFolderIfExists);
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
        Directory.CreateDirectory(Root);
        File.WriteAllText(Ruta, "{basura");
        Assert.Equal("seguir", UserPreferences.Load(Ruta).Tool);

        File.WriteAllText(Ruta, """{"herramienta":"volar","seguirPatron":999,"reencuadreSuavizado":5,"estabilizarIntensidad":-1,"fondoDispositivo":"tpu","reencuadreAspecto":"3:2","ultimaCarpeta":"Z:\\no-existe","carpetaSalida":"relativa\\salida"}""");
        UserPreferences p = UserPreferences.Load(Ruta);
        Assert.Equal("seguir", p.Tool);
        Assert.Equal(81, p.FollowPatternSize);
        Assert.Equal(0.9, p.ReframeSmoothing);
        Assert.Equal(0.1, p.StabilizeIntensity);
        Assert.Equal("auto", p.BackgroundDevice);
        Assert.Equal("9:16", p.ReframeAspect);
        Assert.Null(p.LastFolderIfExists);
        Assert.Null(p.OutputFolder); // Relativa: no vale, junto al video

        // El índice se recorta al selector
        p.ToolIndex = 99;
        Assert.Equal("estabilizar", p.Tool);
    }

    [Fact]
    public void Carpeta_de_salida_se_guarda_y_manda_sobre_junto_al_video()
    {
        UserPreferences prefs = new();
        Assert.Null(prefs.OutputFolder);
        Assert.Equal(UserPreferences.OutputNextToVideoText, prefs.OutputFolderText);

        string salida = PathOf("salidas");
        prefs.OutputFolder = salida + Path.DirectorySeparatorChar + "  "; // Separador final y espacios: se limpian
        prefs.Normalize();
        Assert.Equal(salida, prefs.OutputFolder);
        Assert.Equal(salida, prefs.OutputFolderText);
        prefs.Save(Ruta);
        Assert.Equal(salida, UserPreferences.Load(Ruta).OutputFolder);

        // En blanco vuelve a «junto al video», y un JSON sin el campo (el de la 1.0.0) también
        Assert.Null(UserPreferences.NormalizeFolder("   "));
        Assert.Null(UserPreferences.NormalizeFolder(null));
        File.WriteAllText(Ruta, """{"herramienta":"detectar"}""");
        Assert.Null(UserPreferences.Load(Ruta).OutputFolder);

        // El nombre de salida respeta la carpeta elegida, con el mismo esquema de nombres y sin pisar
        string entrada = PathOf("videos", "clip.mp4");
        Assert.Equal(Path.Combine(salida, "clip-fondo.webm"), OutputNaming.OutputPathFor(entrada, 1, exists: _ => false, outputFolder: salida));
        HashSet<string> ocupados = [Path.Combine(salida, "clip-detecciones.json")];
        Assert.Equal(Path.Combine(salida, "clip-detecciones (2).json"), OutputNaming.OutputPathFor(entrada, 2, exists: ocupados.Contains, outputFolder: salida));
        Assert.Equal(Path.Combine(PathOf("videos"), "clip-fondo.webm"), OutputNaming.OutputPathFor(entrada, 1, exists: _ => false, outputFolder: null));
    }

    [Fact]
    public void Escritura_atomica_compartida_no_deja_temporal_y_pisa_lo_anterior()
    {
        string ruta = PathOf("sub", "archivo.json");
        AtomicFile.WriteAllText(ruta, "{\"a\":1}");
        AtomicFile.WriteAllText(ruta, "{\"a\":2}");
        Assert.Equal("{\"a\":2}", File.ReadAllText(ruta));
        Assert.Single(Directory.GetFiles(PathOf("sub"))); // Sin .tmp

        // La ventana usa la misma escritura
        string ventana = PathOf("ventana.json");
        new WindowPlacement(10, 20, 1300, 850, true).Save(ventana);
        Assert.Equal(new WindowPlacement(10, 20, 1300, 850, true), WindowPlacement.Load(ventana));
        Assert.False(File.Exists(ventana + AtomicFile.TemporarySuffix));
        File.WriteAllText(ventana, "{basura");
        Assert.Null(WindowPlacement.Load(ventana));
        Assert.Null(WindowPlacement.Load(PathOf("no-existe.json")));
    }
}
