using System.Diagnostics;

namespace Rastrear.Core.Ui;

/// <summary>Qué hacer con el archivo resultante: abrir su carpeta con el archivo seleccionado y copiar la ruta.</summary>
public static class ResultActions
{
    /// <summary>Argumentos de <c>explorer.exe</c> para abrir la carpeta con el archivo seleccionado: <c>/select,"C:\…\clip-fondo.webm"</c>.</summary>
    public static string ExplorerSelectArguments(string filePath) => $"/select,\"{Path.GetFullPath(filePath)}\"";

    /// <summary>La carpeta del archivo (o la ruta misma si es una carpeta); null si no existe nada.</summary>
    public static string? FolderOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Directory.Exists(path)) return Path.GetFullPath(path);
        if (!File.Exists(path)) return null;
        return Path.GetDirectoryName(Path.GetFullPath(path));
    }

    /// <summary>Abre el Explorador con el archivo seleccionado; si ya no existe, abre la carpeta; devuelve false si no hay nada que abrir.</summary>
    public static bool OpenFolder(string? filePath, Action<string, string>? start = null)
    {
        start ??= (exe, args) => Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        if (File.Exists(filePath))
        {
            start("explorer.exe", ExplorerSelectArguments(filePath));
            return true;
        }

        string? folder = FolderOf(Path.GetDirectoryName(filePath));
        if (folder is null) return false;
        start("explorer.exe", $"\"{folder}\"");
        return true;
    }

    /// <summary>Texto que se copia al portapapeles: la ruta completa del archivo.</summary>
    public static string ClipboardText(string filePath) => Path.GetFullPath(filePath);
}
