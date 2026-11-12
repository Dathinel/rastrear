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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t  ")]
    public void Archivo_vacio_o_solo_espacios_da_los_valores_por_defecto(string contenido)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Ruta, contenido);
        UserPreferences p = UserPreferences.Load(Ruta);
        Assert.Equal("seguir", p.Tool);
        Assert.Equal(41, p.FollowPatternSize);
        Assert.True(p.NotifyNewVersion);
    }

    [Fact]
    public void Un_tipo_cambiado_descarta_el_archivo_entero()
    {
        Directory.CreateDirectory(Root);
        // «seguirPatron» como texto tumba la lectura completa: no se rescata ni «herramienta»
        File.WriteAllText(Ruta, """{"herramienta":"detectar","seguirPatron":"ochenta","estabilizarIntensidad":0.8}""");
        UserPreferences p = UserPreferences.Load(Ruta);
        Assert.Equal("seguir", p.Tool);
        Assert.Equal(41, p.FollowPatternSize);
        Assert.Equal(0.5, p.StabilizeIntensity);
    }

    [Fact]
    public void Los_campos_desconocidos_de_una_version_futura_no_estorban()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Ruta, """{"herramienta":"reencuadrar","nuevoCampo":{"a":1},"otro":[1,2,3],"numero":42}""");
        UserPreferences p = UserPreferences.Load(Ruta);
        Assert.Equal("reencuadrar", p.Tool);
        Assert.Equal(3, p.ToolIndex);
    }

    [Fact]
    public void Guardar_y_cargar_es_exacto_campo_a_campo()
    {
        UserPreferences prefs = new()
        {
            LastFolder = Root,
            OutputFolder = PathOf("salidas", "final"),
            ToolIndex = 4,
            FollowMode = "region",
            FollowPatternSize = 21,
            BackgroundMask = true,
            BackgroundQuality = "alta",
            BackgroundDevice = "cpu",
            DetectTarget = "personas",
            DetectDevice = "gpu",
            ReframeAspect = "4:5",
            ReframeSmoothing = 0.1,
            StabilizeIntensity = 1.0,
            StabilizeAutoCrop = false,
            NotifyNewVersion = false,
            LastUpdateCheck = new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.FromHours(2)),
            DismissedVersion = "v9.9.9",
        };
        prefs.Save(Ruta);

        UserPreferences leidas = UserPreferences.Load(Ruta);
        Assert.Equal(prefs.LastFolder, leidas.LastFolder);
        Assert.Equal(prefs.OutputFolder, leidas.OutputFolder);
        Assert.Equal(prefs.Tool, leidas.Tool);
        Assert.Equal(prefs.FollowMode, leidas.FollowMode);
        Assert.Equal(prefs.FollowPatternSize, leidas.FollowPatternSize);
        Assert.Equal(prefs.BackgroundMask, leidas.BackgroundMask);
        Assert.Equal(prefs.BackgroundQuality, leidas.BackgroundQuality);
        Assert.Equal(prefs.BackgroundDevice, leidas.BackgroundDevice);
        Assert.Equal(prefs.DetectTarget, leidas.DetectTarget);
        Assert.Equal(prefs.DetectDevice, leidas.DetectDevice);
        Assert.Equal(prefs.ReframeAspect, leidas.ReframeAspect);
        Assert.Equal(prefs.ReframeSmoothing, leidas.ReframeSmoothing);
        Assert.Equal(prefs.StabilizeIntensity, leidas.StabilizeIntensity);
        Assert.Equal(prefs.StabilizeAutoCrop, leidas.StabilizeAutoCrop);
        Assert.Equal(prefs.NotifyNewVersion, leidas.NotifyNewVersion);
        Assert.Equal(prefs.LastUpdateCheck, leidas.LastUpdateCheck);
        Assert.Equal(prefs.DismissedVersion, leidas.DismissedVersion);
    }

    [Theory]
    [InlineData("salida", null)]                                  // relativa: no vale
    [InlineData(".", null)]
    [InlineData("C:", null)]                                      // la carpeta actual de C: (Combine("C:", x) = "C:x")
    [InlineData("C:salida", null)]
    [InlineData("\\salida", null)]                                // relativa a la unidad actual
    [InlineData("C:\\salida", "C:\\salida")]
    [InlineData("C:\\salida\\", "C:\\salida")]                    // sin el separador final
    [InlineData("  C:\\salida  ", "C:\\salida")]                  // sin espacios alrededor
    [InlineData("\\\\servidor\\carpeta", "\\\\servidor\\carpeta")] // UNC vale
    public void NormalizeFolder_solo_queda_rutas_absolutas_y_limpias(string? carpeta, string? esperado) =>
        Assert.Equal(esperado, UserPreferences.NormalizeFolder(carpeta));

    [Fact]
    public void Normalize_arregla_los_valores_que_no_caben_en_un_json()
    {
        UserPreferences p = new()
        {
            ReframeSmoothing = double.NaN,
            StabilizeIntensity = double.PositiveInfinity,
            FollowPatternSize = int.MinValue,
            ToolIndex = -3,
        };
        p.Normalize();
        Assert.Equal(0.5, p.ReframeSmoothing);
        Assert.Equal(0.5, p.StabilizeIntensity);
        Assert.Equal(21, p.FollowPatternSize);
        Assert.Equal("seguir", p.Tool);

        p.ReframeSmoothing = double.NegativeInfinity;
        p.Normalize();
        Assert.Equal(0.5, p.ReframeSmoothing);
    }

    [Fact]
    public void La_ultima_carpeta_solo_vale_si_sigue_siendo_carpeta()
    {
        UserPreferences p = new() { LastFolder = PathOf("archivo.txt") };
        Assert.Null(p.LastFolderIfExists);

        string archivo = Touch("archivo.txt"); // existe, pero es un archivo: tampoco vale
        p.LastFolder = archivo;
        Assert.Null(p.LastFolderIfExists);

        p.LastFolder = Root;
        Assert.Equal(Root, p.LastFolderIfExists);

        p.LastFolder = "";
        Assert.Null(p.LastFolderIfExists);
    }

    [Fact]
    public void Ventana_se_ajusta_a_un_area_de_trabajo_desplazada()
    {
        // Área que no empieza en (0,0): una barra de tareas a la izquierda y arriba
        WindowPlacement? fit = new WindowPlacement(-500, -500, 1200, 700, false).FitInto(100, 50, 1720, 980, 960, 600);
        Assert.Equal(new WindowPlacement(100, 50, 1200, 700, false), fit);

        // Pasada por la derecha y por abajo
        fit = new WindowPlacement(2000, 2000, 1200, 700, false).FitInto(100, 50, 1720, 980, 960, 600);
        Assert.Equal(new WindowPlacement(620, 330, 1200, 700, false), fit);

        // Justo en el mínimo: vale
        fit = new WindowPlacement(0, 0, 960, 600, false).FitInto(100, 50, 1720, 980, 960, 600);
        Assert.Equal(new WindowPlacement(100, 50, 960, 600, false), fit);

        // Un píxel menos de alto: se descarta
        Assert.Null(new WindowPlacement(0, 0, 960, 599, false).FitInto(100, 50, 1720, 980, 960, 600));

        // Área degenerada: se descarta
        Assert.Null(new WindowPlacement(0, 0, 1200, 700, false).FitInto(100, 50, 0, 980, 960, 600));
    }

    [Fact]
    public void Ventana_json_parcial_o_con_campos_de_mas_se_lee_o_se_descarta_en_el_ajuste()
    {
        string ventana = PathOf("ventana.json");
        Directory.CreateDirectory(Root);

        // Un JSON con solo parte de los campos carga con ceros, y el ajuste lo descarta por tamaño
        File.WriteAllText(ventana, """{"x":5,"y":7}""");
        WindowPlacement? p = WindowPlacement.Load(ventana);
        Assert.NotNull(p);
        Assert.Null(p.FitInto(0, 0, 1920, 1080, 960, 600));

        // Campos de más: se ignoran y carga bien
        File.WriteAllText(ventana, """{"x":10,"y":20,"ancho":1300,"alto":850,"maximizada":true,"nuevo":true}""");
        Assert.Equal(new WindowPlacement(10, 20, 1300, 850, true), WindowPlacement.Load(ventana));

        // Tipos cambiados: null, como un archivo roto
        File.WriteAllText(ventana, """{"x":"cien","y":20,"ancho":1300,"alto":850,"maximizada":false}""");
        Assert.Null(WindowPlacement.Load(ventana));
    }
}
