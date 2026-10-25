using System.Text.Json;
using System.Text.Json.Serialization;
using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

/// <summary>
/// Tamaño y posición de la ventana en píxeles físicos, tal como los da AppWindow, guardados en
/// %LOCALAPPDATA%\Dathinel\Rastrear\ventana.json.
/// </summary>
public sealed record WindowPlacement(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("ancho")] int Width,
    [property: JsonPropertyName("alto")] int Height,
    [property: JsonPropertyName("maximizada")] bool Maximized)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dathinel", "Rastrear", "ventana.json");

    /// <summary>Encaja la ventana en el área de trabajo; null si lo guardado es más pequeño que el mínimo (se usa el tamaño por defecto).</summary>
    public WindowPlacement? FitInto(int areaX, int areaY, int areaWidth, int areaHeight, int minWidth, int minHeight)
    {
        if (Width < minWidth || Height < minHeight || areaWidth <= 0 || areaHeight <= 0)
        {
            return null;
        }

        int w = Math.Min(Width, areaWidth);
        int h = Math.Min(Height, areaHeight);
        int x = Math.Clamp(X, areaX, areaX + areaWidth - w);
        int y = Math.Clamp(Y, areaY, areaY + areaHeight - h);
        return this with { X = x, Y = y, Width = w, Height = h };
    }

    public static WindowPlacement? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), RastrearJsonContext.Default.WindowPlacement)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, RastrearJsonContext.Default.WindowPlacement));
    }
}
