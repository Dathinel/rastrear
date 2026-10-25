using Rastrear.Core;
using Rastrear.Core.Vision;

namespace Rastrear.Tests;

public sealed class BusquedaModuloTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "rastrear-busqueda-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _env = new();

    private string LocalAppData => Path.Combine(_raiz, "local");
    private string Base => Path.Combine(_raiz, "app");

    private string Coloca(params string[] carpeta)
    {
        string dir = Path.Combine([_raiz, .. carpeta]);
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, VisionService.ModuleExe);
        File.WriteAllText(exe, "");
        return exe;
    }

    private string Busca() => VisionService.FindModule(null, k => _env.GetValueOrDefault(k), LocalAppData, Base);

    [Fact]
    public void Orden_RASTREAR_MODULOS_Lanzador_Rodaje_RODAJE_MODULOS_junto_al_exe()
    {
        string junto = Coloca("app", "modulos");
        Assert.Equal(junto, Busca());

        string rodajeEnv = Coloca("otros", "modulo-vision");
        _env["RODAJE_MODULOS"] = Path.Combine(_raiz, "otros");
        Assert.Equal(rodajeEnv, Busca());

        string rodaje = Coloca("local", "Rodaje", "complementos", "modulos", "modulo-vision");
        Assert.Equal(rodaje, Busca());

        Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "1.9.0");
        string lanzador = Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "1.10.0");
        Assert.Equal(lanzador, Busca());

        string propio = Coloca("propio");
        _env["RASTREAR_MODULOS"] = Path.Combine(_raiz, "propio");
        Assert.Equal(propio, Busca());
        _env["RASTREAR_MODULOS"] = propio;
        Assert.Equal(propio, Busca());
    }

    [Fact]
    public void Sin_modulo_lista_las_rutas_probadas()
    {
        _env["RASTREAR_MODULOS"] = Path.Combine(_raiz, "nada");
        var ex = Assert.Throws<RastrearUserException>(Busca);
        Assert.Contains(Path.Combine(_raiz, "nada", VisionService.ModuleExe), ex.Message);
        Assert.Contains(Path.Combine(LocalAppData, "Dathinel", "Comun", "modulos", "modulo-vision", "*", VisionService.ModuleExe), ex.Message);
        Assert.Contains(Path.Combine(Base, "modulos", VisionService.ModuleExe), ex.Message);
        Assert.Contains("Lanzador", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }
}
