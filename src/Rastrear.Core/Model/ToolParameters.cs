namespace Rastrear.Core.Model;

/// <summary>
/// Los valores que admiten los parámetros de las herramientas, en un solo sitio: lo que guarda <c>UserPreferences</c>,
/// lo que recibe la Cli y lo que se manda al módulo de visión pasan por aquí, así un JSON editado a mano o una opción
/// mal escrita caen en el valor por omisión en vez de romper algo.
/// </summary>
public static class ToolParameters
{
    public static readonly string[] Devices = ["auto", "gpu", "cpu"];
    public static readonly string[] Qualities = ["normal", "alta"];
    public static readonly string[] DetectTargets = ["rostros", "personas"];
    public static readonly string[] AspectPresets = ["9:16", "1:1", "4:5"];
    public static readonly string[] FollowModes = ["punto", "region"];

    /// <summary>«auto», «gpu» o «cpu» según el índice (0 → «auto», 1 → «gpu», 2 → «cpu», otro → «auto»).</summary>
    public static string DeviceFromIndex(int index) => index switch { 1 => "gpu", 2 => "cpu", _ => "auto" };

    /// <summary>«normal» o «alta» según el índice (1 → «alta», otro → «normal»).</summary>
    public static string QualityFromIndex(int index) => index == 1 ? "alta" : "normal";

    /// <summary>«auto», «gpu» o «cpu»; cualquier otra cosa es «auto».</summary>
    public static string NormalizeDevice(string? device) => Pick(Devices, device);

    /// <summary>Lo que entiende el módulo: «cpu» o «gpu» («auto» es «gpu», que ya cae a CPU si no hay DirectML).</summary>
    public static string ComputeDevice(string? device) => device == "cpu" ? "cpu" : "gpu";

    public static string NormalizeQuality(string? quality) => Pick(Qualities, quality);

    public static string NormalizeDetectTarget(string? target) => Pick(DetectTargets, target);

    public static bool IsDetectTarget(string? target) => Array.IndexOf(DetectTargets, target) >= 0;

    public static string NormalizeAspectPreset(string? preset) => Pick(AspectPresets, preset);

    public static string NormalizeFollowMode(string? mode) => Pick(FollowModes, mode);

    /// <summary>Entre <paramref name="min"/> y <paramref name="max"/>; NaN o infinito (un JSON raro) es <paramref name="fallback"/>.</summary>
    public static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static string Pick(string[] allowed, string? value) =>
        value is not null && Array.IndexOf(allowed, value) >= 0 ? value : allowed[0];
}
