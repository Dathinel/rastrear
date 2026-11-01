namespace Rastrear.Tests;

/// <summary>
/// Carpeta temporal propia de cada prueba (<c>%TEMP%\rastrear-prueba-&lt;guid&gt;</c>), borrada al acabar aunque la
/// prueba falle: lo que antes repetía cada clase con su <c>_raiz</c> y su <c>Dispose</c>.
/// </summary>
public abstract class TemporaryFolderTest : IDisposable
{
    protected string Root { get; } = Path.Combine(Path.GetTempPath(), "rastrear-prueba-" + Guid.NewGuid().ToString("N"));

    /// <summary>Una ruta dentro de la carpeta (sin crear nada).</summary>
    protected string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Crea (o vacía) un archivo dentro de la carpeta, con sus subcarpetas, y devuelve su ruta.</summary>
    protected string Touch(params string[] parts)
    {
        string path = PathOf(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>Crea una subcarpeta y devuelve su ruta.</summary>
    protected string MakeDir(params string[] parts)
    {
        string path = PathOf(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
