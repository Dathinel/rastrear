namespace Rastrear.Core.Ui;

/// <summary>
/// Dónde está el Lanzador de Dathinel en este PC, para abrirlo cuando falta el módulo de visión. Orden:
/// <c>RASTREAR_LANZADOR</c> (exe o carpeta), <c>%LOCALAPPDATA%\Dathinel\Lanzador</c> (directo o en <c>app\</c>, como
/// Rodaje) y <c>%LOCALAPPDATA%\Programs\Lanzador</c>.
/// </summary>
public static class LauncherLocator
{
    public const string ExeName = "Lanzador.exe";

    public static string? Find() => Find(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string? Find(Func<string, string?> env, string localAppData)
    {
        foreach (string candidate in Candidates(env, localAppData))
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static IEnumerable<string> Candidates(Func<string, string?> env, string localAppData)
    {
        if (env("RASTREAR_LANZADOR") is { Length: > 0 } custom)
        {
            yield return custom;
            yield return Path.Combine(custom, ExeName);
        }
        yield return Path.Combine(localAppData, "Dathinel", "Lanzador", ExeName);
        yield return Path.Combine(localAppData, "Dathinel", "Lanzador", "app", ExeName);
        yield return Path.Combine(localAppData, "Programs", "Lanzador", ExeName);
    }
}
