using System.Globalization;
using System.Text.Json;
using Rastrear.Core.Model;
using Rastrear.Core.Tracking;
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

    [Fact]
    public void El_json_del_seguimiento_hace_ida_y_vuelta_exacta()
    {
        FollowResult result = new(1919, 1079, 23.976,
        [
            new(0, 0.0, -5.5, 300.125, 1.0, "Good"),
            new(3, 0.125125, 0.0, 0.0, 0.0, "Lost"), // hueco: no hay cuadros 1 ni 2
        ]);

        using StringWriter writer = new();
        FollowService.WriteJson(result, writer);
        string json = writer.ToString();

        FollowResult? back = JsonSerializer.Deserialize(json, RastrearJsonContext.Default.FollowResult);
        Assert.NotNull(back);
        Assert.Equal(result.Ancho, back.Ancho);
        Assert.Equal(result.Alto, back.Alto);
        Assert.Equal(result.Fps, back.Fps);
        Assert.Equal(2, back.Cuadros.Count);
        Assert.Equal(result.Cuadros[0], back.Cuadros[0]);
        Assert.Equal(result.Cuadros[1], back.Cuadros[1]);

        // Y la segunda escritura es idéntica a la primera
        Assert.Equal(json, JsonSerializer.Serialize(back, RastrearJsonContext.Default.FollowResult));
    }

    [Fact]
    public void El_json_del_seguimiento_usa_los_nombres_en_espanol()
    {
        FollowResult result = new(320, 240, 30.0, [new(0, 0.0, 1.5, 2.5, 0.75, "Good")]);
        using StringWriter writer = new();
        FollowService.WriteJson(result, writer);

        using JsonDocument doc = JsonDocument.Parse(writer.ToString());
        JsonElement root = doc.RootElement;
        Assert.True(root.TryGetProperty("ancho", out _));
        Assert.True(root.TryGetProperty("alto", out _));
        Assert.True(root.TryGetProperty("fps", out _));
        JsonElement cuadro = root.GetProperty("cuadros")[0];
        Assert.True(cuadro.TryGetProperty("cuadro", out _));
        Assert.True(cuadro.TryGetProperty("t", out _));
        Assert.True(cuadro.TryGetProperty("x", out _));
        Assert.True(cuadro.TryGetProperty("y", out _));
        Assert.True(cuadro.TryGetProperty("confianza", out _));
        Assert.True(cuadro.TryGetProperty("estado", out _));
    }

    [Fact]
    public void El_json_de_detecciones_hace_ida_y_vuelta_y_tolera_campos_de_mas()
    {
        DetectionResult result = new(
            [new([new(0.1, 0.2, 0.3, 0.4, 0.95)]), new([])],
            Dispositivo: "cpu",
            Segundos: 1.25);

        string json = JsonSerializer.Serialize(result, RastrearJsonContext.Default.DetectionResult);
        DetectionResult? back = JsonSerializer.Deserialize(json, RastrearJsonContext.Default.DetectionResult);
        Assert.NotNull(back);
        Assert.Equal(2, back.Cuadros.Count);
        Assert.Equal(new DetectionBox(0.1, 0.2, 0.3, 0.4, 0.95), back.Cuadros[0].Cajas[0]);
        Assert.Empty(back.Cuadros[1].Cajas);
        Assert.Equal("cpu", back.Dispositivo);
        Assert.Equal(1.25, back.Segundos);
        Assert.Equal(json, JsonSerializer.Serialize(back, RastrearJsonContext.Default.DetectionResult));

        // Campos que no existen (de una versión futura) se ignoran; los opcionales que faltan quedan null
        DetectionResult? futuro = JsonSerializer.Deserialize(
            """{"cuadros":[{"cajas":[],"extra":1}],"dispositivo":"gpu","campoNuevo":{"a":[1,2]},"segundos":0.5}""",
            RastrearJsonContext.Default.DetectionResult);
        Assert.NotNull(futuro);
        Assert.Single(futuro.Cuadros);
        Assert.Equal("gpu", futuro.Dispositivo);
        Assert.Equal(0.5, futuro.Segundos);

        DetectionResult? minimas = JsonSerializer.Deserialize("""{"cuadros":[]}""", RastrearJsonContext.Default.DetectionResult);
        Assert.NotNull(minimas);
        Assert.Null(minimas.Dispositivo);
        Assert.Null(minimas.Segundos);
    }

    [Theory]
    [InlineData("es-CO")]
    [InlineData("de-DE")]
    public void Los_resultados_salen_igual_con_cultura_de_coma_decimal(string culture)
    {
        FollowResult result = new(320, 240, 30.0,
        [
            new(0, 0.0, 100.25, 200.5, 1.0, "Good"),
            new(1, 0.033, 103.5, 202.1, 0.98, "Good"),
        ]);

        static string Serializar(FollowResult r)
        {
            using StringWriter writer = new();
            FollowService.WriteJson(r, writer);
            return writer.ToString();
        }

        string enIngles = Serializar(result);
        string enCultura;
        CultureInfo anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); // «,» como separador decimal
            enCultura = Serializar(result);
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }

        Assert.Equal(enIngles, enCultura);
        Assert.Contains("100.25", enCultura);
        Assert.DoesNotContain("100,25", enCultura);
    }

    [Fact]
    public void Nombres_de_salida_con_puntos_espacios_tildes_y_sin_extension()
    {
        // Con puntos: solo cae la última extensión
        string entrada = Path.Combine("carpeta", "mi.video v2 (final).mp4");
        string dir = Path.GetDirectoryName(Path.GetFullPath(entrada))!;
        Assert.Equal(
            Path.Combine(dir, "mi.video v2 (final)-fondo.webm"),
            OutputNaming.OutputPathFor(entrada, 1, exists: _ => false));

        // Sin extensión
        entrada = Path.Combine("carpeta", "solo-nombre");
        Assert.Equal(
            Path.Combine(dir, "solo-nombre-fondo.webm"),
            OutputNaming.OutputPathFor(entrada, 1, exists: _ => false));

        // Tildes, eñes y espacios se conservan tal cual
        entrada = Path.Combine("carpeta con ñ", "cumpleaños niño.mov");
        string dirTildes = Path.GetDirectoryName(Path.GetFullPath(entrada))!;
        Assert.Equal(
            Path.Combine(dirTildes, "cumpleaños niño-detecciones.json"),
            OutputNaming.OutputPathFor(entrada, 2, exists: _ => false));

        // Colisión también sobre nombres raros
        HashSet<string> ocupados = [Path.Combine(dirTildes, "cumpleaños niño-detecciones.json")];
        Assert.Equal(
            Path.Combine(dirTildes, "cumpleaños niño-detecciones (2).json"),
            OutputNaming.OutputPathFor(entrada, 2, exists: ocupados.Contains));

        // Nombre muy largo: solo se arma la ruta, sin tocar el disco
        string largo = new('a', 200);
        entrada = Path.Combine("carpeta", largo + ".mp4");
        string salida = OutputNaming.OutputPathFor(entrada, 4, exists: _ => false);
        Assert.EndsWith(largo + "-estabilizado.mp4", salida);
    }

    [Fact]
    public void Nombre_de_salida_con_carpeta_inexistente_o_relativa()
    {
        string entrada = Path.Combine("videos", "clip.mp4");

        // La carpeta de salida no existe: la ruta sale dentro de ella igual (el error vendrá al escribir)
        string inexistente = PathOf("no-existe", "salidas");
        Assert.Equal(
            Path.Combine(inexistente, "clip-fondo.webm"),
            OutputNaming.OutputPathFor(entrada, 1, exists: _ => false, outputFolder: inexistente));

        // Una carpeta de salida relativa se resuelve a absoluta (contra el directorio actual)
        string relativa = OutputNaming.OutputPathFor(entrada, 1, exists: _ => false, outputFolder: Path.Combine("salidas-relativas"));
        Assert.True(Path.IsPathRooted(relativa));
        Assert.EndsWith(Path.Combine("salidas-relativas", "clip-fondo.webm"), relativa);

        // Espacios alrededor de la carpeta: cae junto al video
        string dirEntrada = Path.GetDirectoryName(Path.GetFullPath(entrada))!;
        Assert.Equal(
            Path.Combine(dirEntrada, "clip-fondo.webm"),
            OutputNaming.OutputPathFor(entrada, 1, exists: _ => false, outputFolder: "   "));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void Herramienta_fuera_de_rango_lanza_en_el_nombre_de_salida(int toolIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputNaming.ForTool(toolIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputNaming.OutputPathFor("clip.mp4", toolIndex));
    }
}
