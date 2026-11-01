using Rastrear.Core.Ui;

namespace Rastrear.Tests;

/// <summary>Aviso de versión nueva: parseo del JSON de GitHub, comparación con la app, 24 h y «Más tarde», sin red.</summary>
public sealed class ActualizacionesTests : TemporaryFolderTest
{
    private static readonly Version Actual = new(1, 0, 0);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Json(string tag, string? url = "https://github.com/Dathinel/rastrear/releases/tag/" + "v1.1.0") =>
        $$"""{"tag_name":"{{tag}}","name":"Rastrear {{tag}}","html_url":{{(url is null ? "null" : $"\"{url}\"")}},"draft":false,"prerelease":false,"assets":[{"name":"x.zip"}]}""";

    /// <summary>Fuente falsa: devuelve el JSON dado o lanza como si no hubiera red; cuenta las consultas.</summary>
    private sealed class FuenteFalsa(string? json) : IReleaseFeed
    {
        public int Consultas { get; private set; }

        public Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
        {
            Consultas++;
            return json is null
                ? Task.FromException<string>(new HttpRequestException("sin red"))
                : Task.FromResult(json);
        }
    }

    [Fact]
    public void Etiqueta_vX_Y_Z_y_otras_formas()
    {
        Assert.True(UpdateChecker.TryParseTag("v1.2.3", out Version? v));
        Assert.Equal(new Version(1, 2, 3), v);
        Assert.True(UpdateChecker.TryParseTag(" V2.0.10 ", out v));
        Assert.Equal(new Version(2, 0, 10), v);
        Assert.True(UpdateChecker.TryParseTag("1.1", out v));
        Assert.Equal(new Version(1, 1, 0), v);

        foreach (string raro in new[] { "", "v", "1", "beta", "v1.1.0-rc1", "1.2.3.4", "v1..2", "1.2.x", "ffmpeg-n9.0.2" })
        {
            Assert.False(UpdateChecker.TryParseTag(raro, out _));
        }

        Assert.Equal("1.0.0", UpdateChecker.Format(new Version(1, 0)));
        Assert.Equal("1.2.3", UpdateChecker.Format(new Version(1, 2, 3, 4)));
        Assert.Equal(new Version(1, 0, 0), UpdateChecker.CurrentVersion); // Directory.Build.props
    }

    [Fact]
    public void Parseo_del_json_de_github_y_de_json_raro()
    {
        ReleaseInfo? r = UpdateChecker.ParseRelease(Json("v1.1.0"));
        Assert.NotNull(r);
        Assert.Equal("v1.1.0", r.Tag);
        Assert.Equal(new Version(1, 1, 0), r.Version);
        Assert.Equal("https://github.com/Dathinel/rastrear/releases/tag/v1.1.0", r.Url);
        Assert.Equal("Rastrear 1.1.0 disponible", r.BannerText);
        Assert.Contains("Lanzador", UpdateChecker.BannerDetail(Actual, launcherFound: true));
        Assert.DoesNotContain("Lanzador", UpdateChecker.BannerDetail(Actual, launcherFound: false));
        Assert.Contains("1.0.0", UpdateChecker.BannerDetail(Actual, launcherFound: false));

        // Sin html_url o con una que no es de github.com por https: la página de la etiqueta en el repositorio
        Assert.Equal("https://github.com/Dathinel/rastrear/releases/tag/v1.1.0", UpdateChecker.ParseRelease(Json("v1.1.0", url: null))!.Url);
        Assert.Equal("https://github.com/Dathinel/rastrear/releases/tag/v1.1.0", UpdateChecker.ParseRelease(Json("v1.1.0", url: "http://otro.example/x"))!.Url);

        Assert.Null(UpdateChecker.ParseRelease(null));
        Assert.Null(UpdateChecker.ParseRelease("   "));
        Assert.Null(UpdateChecker.ParseRelease("<html>no soy json</html>"));
        Assert.Null(UpdateChecker.ParseRelease("{basura"));
        Assert.Null(UpdateChecker.ParseRelease("[]"));
        Assert.Null(UpdateChecker.ParseRelease("null"));
        Assert.Null(UpdateChecker.ParseRelease("""{"message":"Not Found","documentation_url":"https://docs.github.com"}"""));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"beta"}"""));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"v1.1.0","prerelease":true}"""));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":"v1.1.0","draft":true}"""));
        Assert.Null(UpdateChecker.ParseRelease("""{"tag_name":123}"""));
    }

    [Theory]
    [InlineData("v1.1.0", UpdateCheckOutcome.NewVersion)]
    [InlineData("v2.0.0", UpdateCheckOutcome.NewVersion)]
    [InlineData("v1.0.1", UpdateCheckOutcome.NewVersion)]
    [InlineData("v1.0.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.0.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("v0.9.9", UpdateCheckOutcome.UpToDate)]
    [InlineData("v1.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("rara", UpdateCheckOutcome.Failed)]
    public async Task Nueva_igual_menor_o_tag_raro(string tag, UpdateCheckOutcome esperado)
    {
        UserPreferences prefs = new();
        FuenteFalsa fuente = new(Json(tag));
        UpdateCheckResult r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora);
        Assert.Equal(esperado, r.Outcome);
        Assert.Equal(1, fuente.Consultas);
        Assert.Equal(Ahora, prefs.LastUpdateCheck); // Se apunta la consulta aunque no sirva de nada
        if (esperado == UpdateCheckOutcome.Failed) Assert.Null(r.Release); else Assert.NotNull(r.Release);
    }

    [Fact]
    public async Task Sin_red_silencio_y_la_consulta_queda_apuntada()
    {
        UserPreferences prefs = new();
        FuenteFalsa fuente = new(null);
        UpdateCheckResult r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora);
        Assert.Equal(UpdateCheckOutcome.Failed, r.Outcome);
        Assert.Null(r.Release);
        Assert.Equal(Ahora, prefs.LastUpdateCheck);
        Assert.Contains("No se pudo consultar", UpdateChecker.ManualCheckText(r, Actual));
    }

    [Fact]
    public async Task Descartada_con_mas_tarde_no_vuelve_a_avisar_salvo_al_buscar_a_mano()
    {
        UserPreferences prefs = new() { DismissedVersion = "v1.1.0" };
        UpdateCheckResult r = await UpdateChecker.CheckAsync(new FuenteFalsa(Json("v1.1.0")), Actual, prefs, Ahora);
        Assert.Equal(UpdateCheckOutcome.Dismissed, r.Outcome);
        Assert.NotNull(r.Release);

        // Otra versión mayor sí avisa
        prefs.LastUpdateCheck = null;
        r = await UpdateChecker.CheckAsync(new FuenteFalsa(Json("v1.2.0")), Actual, prefs, Ahora);
        Assert.Equal(UpdateCheckOutcome.NewVersion, r.Outcome);

        // «Buscar actualizaciones» pasa por encima del descarte
        r = await UpdateChecker.CheckAsync(new FuenteFalsa(Json("v1.1.0")), Actual, prefs, Ahora, force: true);
        Assert.Equal(UpdateCheckOutcome.NewVersion, r.Outcome);
        Assert.Equal("Rastrear 1.1.0 disponible", UpdateChecker.ManualCheckText(r, Actual));
    }

    [Fact]
    public async Task Como_mucho_una_vez_cada_24_horas_y_solo_con_el_aviso_activado()
    {
        UserPreferences prefs = new() { LastUpdateCheck = Ahora - TimeSpan.FromHours(23) };
        FuenteFalsa fuente = new(Json("v1.1.0"));

        UpdateCheckResult r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora);
        Assert.Equal(UpdateCheckOutcome.Skipped, r.Outcome);
        Assert.Equal(0, fuente.Consultas);
        Assert.Equal(Ahora - TimeSpan.FromHours(23), prefs.LastUpdateCheck); // No se toca

        // Pasadas las 24 h sí
        r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora + TimeSpan.FromHours(1));
        Assert.Equal(UpdateCheckOutcome.NewVersion, r.Outcome);
        Assert.Equal(1, fuente.Consultas);
        Assert.Equal(Ahora + TimeSpan.FromHours(1), prefs.LastUpdateCheck);

        // Fecha en el futuro (el reloj fue hacia atrás): se consulta
        Assert.True(UpdateChecker.IsDue(Ahora + TimeSpan.FromDays(2), Ahora));
        Assert.True(UpdateChecker.IsDue(null, Ahora));
        Assert.False(UpdateChecker.IsDue(Ahora - TimeSpan.FromMinutes(1), Ahora));

        // Con el aviso apagado no se consulta al arrancar, pero «Buscar actualizaciones» sí
        prefs = new UserPreferences { NotifyNewVersion = false };
        r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora);
        Assert.Equal(UpdateCheckOutcome.Skipped, r.Outcome);
        Assert.Null(prefs.LastUpdateCheck);
        prefs.LastUpdateCheck = Ahora;
        r = await UpdateChecker.CheckAsync(fuente, Actual, prefs, Ahora, force: true);
        Assert.Equal(UpdateCheckOutcome.NewVersion, r.Outcome);
        Assert.Equal(2, fuente.Consultas);

        // Al día: el texto de «Buscar actualizaciones» lo dice
        r = await UpdateChecker.CheckAsync(new FuenteFalsa(Json("v1.0.0")), Actual, prefs, Ahora, force: true);
        Assert.Equal("No hay nada nuevo: la 1.0.0 es la última versión.", UpdateChecker.ManualCheckText(r, Actual));
    }

    [Fact]
    public void Las_preferencias_guardan_el_interruptor_la_consulta_y_la_descartada()
    {
        string ruta = PathOf("preferencias.json");

        UserPreferences porDefecto = UserPreferences.Load(ruta);
        Assert.True(porDefecto.NotifyNewVersion);
        Assert.Null(porDefecto.LastUpdateCheck);
        Assert.Null(porDefecto.DismissedVersion);

        new UserPreferences { NotifyNewVersion = false, LastUpdateCheck = Ahora, DismissedVersion = "v1.1.0" }.Save(ruta);
        UserPreferences leidas = UserPreferences.Load(ruta);
        Assert.False(leidas.NotifyNewVersion);
        Assert.Equal(Ahora, leidas.LastUpdateCheck);
        Assert.Equal("v1.1.0", leidas.DismissedVersion);

        // Un preferencias.json de la 1.0.0 (sin estos campos) parte de los valores por defecto
        File.WriteAllText(ruta, """{"herramienta":"detectar","versionDescartada":"  "}""");
        UserPreferences viejas = UserPreferences.Load(ruta);
        Assert.True(viejas.NotifyNewVersion);
        Assert.Null(viejas.LastUpdateCheck);
        Assert.Null(viejas.DismissedVersion);
        Assert.Equal("detectar", viejas.Tool);
    }
}
