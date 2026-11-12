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
        try
        {
            File.WriteAllText(tmp, contents);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Sin esto, un destino de solo lectura o un disco lleno dejan el temporal ahí para siempre.
            try { File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
