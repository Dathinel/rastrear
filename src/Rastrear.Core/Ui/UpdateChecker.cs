using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

/// <summary>De dónde sale el JSON de la última release: GitHub en la app, un texto fijo en las pruebas.</summary>
public interface IReleaseFeed
{
    /// <summary>El JSON de <c>releases/latest</c>; lanza (red, tiempo límite, HTTP) si no se pudo consultar.</summary>
    Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken);
}

/// <summary>La API pública de GitHub, sin token: <c>User-Agent: Rastrear/&lt;versión&gt;</c> y 5 s de tiempo límite.</summary>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Dathinel/rastrear/releases/latest";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _client;

    public GitHubReleaseFeed(Version appVersion)
    {
        _client = new HttpClient { Timeout = Timeout };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Rastrear", UpdateChecker.Format(appVersion)));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<string> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lo que viene de GitHub en <c>releases/latest</c>; solo los campos que se usan.</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
}

/// <summary>Una versión publicada: la etiqueta tal cual (<c>v1.1.0</c>), la versión y la página de la release.</summary>
public sealed record ReleaseInfo(string Tag, Version Version, string Url)
{
    /// <summary>Texto de la franja: «Rastrear 1.1.0 disponible».</summary>
    public string BannerText => $"Rastrear {UpdateChecker.Format(Version)} disponible";
}

public enum UpdateCheckOutcome
{
    /// <summary>No tocaba (aviso apagado o menos de 24 h desde la última consulta): no se consultó.</summary>
    Skipped,
    /// <summary>Sin red, tiempo límite, HTTP distinto de 200 o un JSON raro: silencio.</summary>
    Failed,
    /// <summary>La última release es igual o menor que la app.</summary>
    UpToDate,
    /// <summary>Hay una versión mayor pero el usuario ya dijo «Más tarde» a esa misma.</summary>
    Dismissed,
    /// <summary>Hay una versión mayor que la app.</summary>
    NewVersion,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, ReleaseInfo? Release)
{
    public static readonly UpdateCheckResult Skipped = new(UpdateCheckOutcome.Skipped, null);
    public static readonly UpdateCheckResult Failed = new(UpdateCheckOutcome.Failed, null);
}

/// <summary>
/// Aviso de versión nueva. Al arrancar se consulta como mucho una vez cada 24 h (la hora de la última consulta va en las
/// preferencias) y solo si el aviso está activado; «Buscar actualizaciones» consulta al momento y pasa por encima de las
/// 24 h y de la versión descartada con «Más tarde». Cualquier fallo de red o de formato es silencio: la app no depende de esto.
/// </summary>
public static class UpdateChecker
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(24);
    public const string ReleasesUrl = "https://github.com/Dathinel/rastrear/releases";

    /// <summary>La versión de la app (tres cifras): la misma en Core, Cli y App por <c>Directory.Build.props</c>.</summary>
    public static Version CurrentVersion => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary><c>1.1.0</c>: siempre tres cifras, aunque la versión venga con dos o con cuatro.</summary>
    public static string Format(Version version) => Normalize(version).ToString(3);

    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>¿Toca consultar? Sin consulta previa, pasadas 24 h, o con una fecha futura (el reloj fue hacia atrás).</summary>
    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is not { } last || last > now || now - last >= MinimumInterval;

    /// <summary>
    /// Consulta y decide. <paramref name="force"/> es «Buscar actualizaciones»: ignora el interruptor, las 24 h y «Más tarde».
    /// Apunta la hora de la consulta en <paramref name="prefs"/> (quien llama guarda) siempre que se intentó.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(
        IReleaseFeed feed, Version current, UserPreferences prefs, DateTimeOffset now, bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && (!prefs.NotifyNewVersion || !IsDue(prefs.LastUpdateCheck, now)))
        {
            return UpdateCheckResult.Skipped;
        }

        prefs.LastUpdateCheck = now;
        string json;
        try
        {
            json = await feed.FetchLatestReleaseJsonAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return UpdateCheckResult.Failed;
        }

        return Decide(ParseRelease(json), current, force ? null : prefs.DismissedVersion);
    }

    /// <summary>La parte sin red: compara la release leída con la app y con lo descartado.</summary>
    public static UpdateCheckResult Decide(ReleaseInfo? release, Version current, string? dismissedTag)
    {
        if (release is null) return UpdateCheckResult.Failed;
        if (release.Version <= Normalize(current)) return new UpdateCheckResult(UpdateCheckOutcome.UpToDate, release);
        bool dismissed = dismissedTag is not null && string.Equals(dismissedTag, release.Tag, StringComparison.OrdinalIgnoreCase);
        return new UpdateCheckResult(dismissed ? UpdateCheckOutcome.Dismissed : UpdateCheckOutcome.NewVersion, release);
    }

    /// <summary>
    /// Lee el JSON de GitHub; null si no es JSON, no es una release publicada o la etiqueta no es <c>vX.Y.Z</c> (ni <c>X.Y.Z</c>).
    /// La página de la release solo se acepta si es https en github.com; si no, la de esa etiqueta en el repositorio.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        GitHubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize(json, RastrearJsonContext.Default.GitHubRelease);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release is null || release.Draft || release.Prerelease || !TryParseTag(release.TagName, out Version? version))
        {
            return null;
        }

        string tag = release.TagName!.Trim();
        string url = release.HtmlUrl is { } u && Uri.TryCreate(u, UriKind.Absolute, out Uri? uri)
                     && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : $"{ReleasesUrl}/tag/{Uri.EscapeDataString(tag)}";
        return new ReleaseInfo(tag, version, url);
    }

    /// <summary><c>v1.2.3</c> o <c>1.2.3</c> (también <c>1.2</c>); nada de sufijos, espacios ni otras letras.</summary>
    public static bool TryParseTag(string? tag, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;
        ReadOnlySpan<char> s = tag.AsSpan().Trim();
        if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        if (s.IsEmpty) return false;

        Span<int> parts = stackalloc int[3];
        int count = 0;
        foreach (Range range in s.Split('.'))
        {
            ReadOnlySpan<char> piece = s[range];
            if (count == 3 || piece.IsEmpty || piece.Length > 6 ||
                !int.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                return false;
            }
            parts[count++] = n;
        }
        if (count < 2) return false;
        version = new Version(parts[0], parts[1], count == 3 ? parts[2] : 0);
        return true;
    }

    /// <summary>Segunda línea de la franja: qué versión se tiene y quién instala la nueva.</summary>
    public static string BannerDetail(Version current, bool launcherFound) => launcherFound
        ? $"Tienes la {Format(current)}. El Lanzador instala la versión nueva."
        : $"Tienes la {Format(current)}. El zip y el instalador están en la página de la versión.";

    /// <summary>Texto para «Buscar actualizaciones» cuando no hay franja que mostrar.</summary>
    public static string ManualCheckText(UpdateCheckResult result, Version current) => result.Outcome switch
    {
        UpdateCheckOutcome.NewVersion or UpdateCheckOutcome.Dismissed => result.Release!.BannerText,
        UpdateCheckOutcome.UpToDate => $"No hay nada nuevo: la {Format(current)} es la última versión.",
        _ => "No se pudo consultar GitHub. Comprueba la conexión y vuelve a intentarlo.",
    };
}
