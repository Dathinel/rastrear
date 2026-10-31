namespace Rastrear.Core.Vision;

/// <summary>No se encontró <c>Rodaje.Module.Vision.exe</c> en ninguna de las rutas probadas: hay que instalar «modulo-vision».</summary>
public sealed class VisionModuleMissingException : RastrearUserException
{
    public IReadOnlyList<string> TriedPaths { get; }

    public VisionModuleMissingException(IReadOnlyList<string> triedPaths)
        : base("No se encontró el módulo de visión (Rodaje.Module.Vision.exe). Rutas probadas:\n  " + string.Join("\n  ", triedPaths) +
               "\nInstala «modulo-vision» con el Lanzador, pasa --modulo <ruta> o define la variable RASTREAR_MODULOS.")
    {
        TriedPaths = triedPaths;
    }

    /// <summary>Lo que ve quien usa la ventana: qué falta, para qué hace falta y cómo se arregla, sin rutas.</summary>
    public static string UserMessage(bool launcherFound) =>
        "Falta el módulo de visión de Rodaje («modulo-vision»), que necesitan «Quitar fondo», «Detectar» y «Reencuadrar». " +
        (launcherFound
            ? "Instálalo desde el Lanzador y vuelve a intentarlo."
            : "Instálalo con el Lanzador de Dathinel (no se encontró en este PC) o define RASTREAR_MODULOS con su carpeta.");
}
