using System.Text.Json;
using System.Text.Json.Serialization;
using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

/// <summary>
/// Lo que la ventana recuerda entre sesiones: la última carpeta de la que se abrió un video, la última herramienta
/// y los parámetros de cada una. Se guarda en %LOCALAPPDATA%\Dathinel\Rastrear\preferencias.json; si el archivo
/// falta o está roto se parte de los valores por defecto.
/// </summary>
public sealed class UserPreferences
{
    /// <summary>Nombres de las herramientas en el orden del selector de la ventana (los mismos verbos que la Cli).</summary>
    public static readonly string[] ToolNames = ["seguir", "quitar-fondo", "detectar", "reencuadrar", "estabilizar"];

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dathinel", "Rastrear", "preferencias.json");

    [JsonPropertyName("ultimaCarpeta")] public string? LastFolder { get; set; }
    [JsonPropertyName("herramienta")] public string Tool { get; set; } = ToolNames[0];

    [JsonPropertyName("seguirModo")] public string FollowMode { get; set; } = "punto";
    [JsonPropertyName("seguirPatron")] public int FollowPatternSize { get; set; } = 41;

    [JsonPropertyName("fondoMascara")] public bool BackgroundMask { get; set; }
    [JsonPropertyName("fondoCalidad")] public string BackgroundQuality { get; set; } = "normal";
    [JsonPropertyName("fondoDispositivo")] public string BackgroundDevice { get; set; } = "auto";

    [JsonPropertyName("detectarObjetivo")] public string DetectTarget { get; set; } = "rostros";
    [JsonPropertyName("detectarDispositivo")] public string DetectDevice { get; set; } = "auto";

    [JsonPropertyName("reencuadreAspecto")] public string ReframeAspect { get; set; } = "9:16";
    [JsonPropertyName("reencuadreSuavizado")] public double ReframeSmoothing { get; set; } = 0.5;

    [JsonPropertyName("estabilizarIntensidad")] public double StabilizeIntensity { get; set; } = 0.5;
    [JsonPropertyName("estabilizarRecorte")] public bool StabilizeAutoCrop { get; set; } = true;

    /// <summary>«Avisar cuando haya una versión nueva»: al arrancar se consulta GitHub (<see cref="UpdateChecker"/>).</summary>
    [JsonPropertyName("avisarVersionNueva")] public bool NotifyNewVersion { get; set; } = true;
    /// <summary>Cuándo se consultó por última vez: como mucho una vez cada 24 h.</summary>
    [JsonPropertyName("ultimaConsultaVersion")] public DateTimeOffset? LastUpdateCheck { get; set; }
    /// <summary>Etiqueta (<c>v1.1.0</c>) a la que se dijo «Más tarde»: no se vuelve a avisar de esa.</summary>
    [JsonPropertyName("versionDescartada")] public string? DismissedVersion { get; set; }

    /// <summary>Índice de la herramienta en el selector de la ventana; 0 si el nombre guardado no existe.</summary>
    [JsonIgnore]
    public int ToolIndex
    {
        get => Math.Max(0, Array.IndexOf(ToolNames, Tool));
        set => Tool = ToolNames[Math.Clamp(value, 0, ToolNames.Length - 1)];
    }

    /// <summary>La última carpeta, solo si sigue existiendo (una unidad extraíble puede haberse ido).</summary>
    [JsonIgnore]
    public string? LastFolderIfExists => LastFolder is { Length: > 0 } f && Directory.Exists(f) ? f : null;

    /// <summary>Apunta la carpeta del archivo abierto o guardado.</summary>
    public void RememberFile(string filePath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
        {
            LastFolder = dir;
        }
    }

    /// <summary>Deja cada valor dentro de lo que admiten los controles: un JSON editado a mano no debe romper un deslizador.</summary>
    public UserPreferences Normalize()
    {
        if (Array.IndexOf(ToolNames, Tool) < 0) Tool = ToolNames[0];
        if (FollowMode is not ("punto" or "region")) FollowMode = "punto";
        FollowPatternSize = Math.Clamp(FollowPatternSize, 21, 81);
        if (BackgroundQuality is not ("normal" or "alta")) BackgroundQuality = "normal";
        BackgroundDevice = NormalizeDevice(BackgroundDevice);
        if (DetectTarget is not ("rostros" or "personas")) DetectTarget = "rostros";
        DetectDevice = NormalizeDevice(DetectDevice);
        if (ReframeAspect is not ("9:16" or "1:1" or "4:5")) ReframeAspect = "9:16";
        ReframeSmoothing = ClampFinite(ReframeSmoothing, 0.1, 0.9, 0.5);
        StabilizeIntensity = ClampFinite(StabilizeIntensity, 0.1, 1.0, 0.5);
        if (string.IsNullOrWhiteSpace(DismissedVersion)) DismissedVersion = null;
        return this;
    }

    private static string NormalizeDevice(string device) => device is "auto" or "gpu" or "cpu" ? device : "auto";

    private static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static UserPreferences Load(string path)
    {
        try
        {
            if (File.Exists(path) &&
                JsonSerializer.Deserialize(File.ReadAllText(path), RastrearJsonContext.Default.UserPreferences) is { } loaded)
            {
                return loaded.Normalize();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Archivo roto o sin permiso: valores por defecto.
        }

        return new UserPreferences();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Temporal y luego Move: un apagón a medio escribir no deja el JSON roto.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, RastrearJsonContext.Default.UserPreferences));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Guarda sin fallar: sin permiso o con el disco lleno, la próxima vez se parte de lo anterior.</summary>
    public bool TrySave(string path)
    {
        try
        {
            Save(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
