namespace Rastrear.Core.Ui;

/// <summary>
/// Nombre del archivo que produce cada herramienta, junto al video de entrada o en la carpeta de salida elegida:
/// <c>clip-fondo.webm</c>, <c>clip-seguimiento.csv</c>… Si ya existe, <c>clip-fondo (2).webm</c>, sin pisar nada.
/// Lo usan la ventana y la Cli.
/// </summary>
public static class OutputNaming
{
    /// <summary>Sufijo y extensión por herramienta (índice del selector) y modo.</summary>
    public static (string Suffix, string Extension) ForTool(int toolIndex, bool backgroundMask = false) => toolIndex switch
    {
        0 => ("seguimiento", ".csv"),
        1 => backgroundMask ? ("mascara", ".mp4") : ("fondo", ".webm"),
        2 => ("detecciones", ".json"),
        3 => ("reencuadrado", ".mp4"),
        4 => ("estabilizado", ".mp4"),
        _ => throw new ArgumentOutOfRangeException(nameof(toolIndex)),
    };

    /// <summary>
    /// Ruta de salida sin colisionar con un archivo que exista (<paramref name="exists"/>, por defecto el disco):
    /// en <paramref name="outputFolder"/> si se da, si no junto a la entrada.
    /// </summary>
    public static string OutputPathFor(string inputPath, string suffix, string extension, Func<string, bool>? exists = null, string? outputFolder = null)
    {
        exists ??= File.Exists;
        string dir = string.IsNullOrWhiteSpace(outputFolder)
            ? Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? string.Empty
            : Path.GetFullPath(outputFolder);
        string stem = Path.GetFileNameWithoutExtension(inputPath) + "-" + suffix;
        string candidate = Path.Combine(dir, stem + extension);
        for (int n = 2; exists(candidate); n++)
        {
            candidate = Path.Combine(dir, $"{stem} ({n}){extension}");
        }
        return candidate;
    }

    public static string OutputPathFor(string inputPath, int toolIndex, bool backgroundMask = false, Func<string, bool>? exists = null, string? outputFolder = null)
    {
        (string suffix, string ext) = ForTool(toolIndex, backgroundMask);
        return OutputPathFor(inputPath, suffix, ext, exists, outputFolder);
    }
}
