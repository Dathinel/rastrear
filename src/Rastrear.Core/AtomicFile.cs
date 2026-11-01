namespace Rastrear.Core;

/// <summary>
/// Escritura de un archivo pequeño (los JSON de preferencias y de la ventana) que no deja nada a medias: se escribe
/// en un temporal junto al destino y se mueve encima, así un apagón a medio escribir deja el archivo anterior entero.
/// Crea la carpeta si falta.
/// </summary>
public static class AtomicFile
{
    public const string TemporarySuffix = ".tmp";

    public static void WriteAllText(string path, string contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = path + TemporarySuffix;
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
