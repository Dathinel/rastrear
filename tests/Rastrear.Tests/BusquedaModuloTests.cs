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
        var ex = Assert.Throws<VisionModuleMissingException>(Busca);
        Assert.IsAssignableFrom<RastrearUserException>(ex);
        Assert.Contains(Path.Combine(_raiz, "nada", VisionService.ModuleExe), ex.TriedPaths);
        Assert.Contains(Path.Combine(_raiz, "nada", VisionService.ModuleExe), ex.Message);
        Assert.Contains(Path.Combine(LocalAppData, "Dathinel", "Comun", "modulos", "modulo-vision", "*", VisionService.ModuleExe), ex.Message);
        Assert.Contains(Path.Combine(Base, "modulos", VisionService.ModuleExe), ex.Message);
        Assert.Contains("Lanzador", ex.Message);
    }

    [Fact]
    public void Ruta_propia_gana_a_todo_y_la_que_falla_no_sigue_buscando()
    {
        // Un exe suelto como ruta propia vale aunque haya módulo en todas partes
        Coloca("app", "modulos");
        string propio = Coloca("elegida");
        Assert.Equal(Path.GetFullPath(propio), VisionService.FindModule(propio, k => _env.GetValueOrDefault(k), LocalAppData, Base));

        // Una carpeta propia con el exe dentro también
        Assert.Equal(Path.GetFullPath(propio), VisionService.FindModule(Path.GetDirectoryName(propio)!, k => _env.GetValueOrDefault(k), LocalAppData, Base));

        // La ruta propia que no existe NO cae a las demás: el usuario la eligió y el error la nombra
        string mala = Path.Combine(_raiz, "no-esta", VisionService.ModuleExe);
        var ex = Assert.Throws<RastrearUserException>(() =>
            VisionService.FindModule(mala, k => _env.GetValueOrDefault(k), LocalAppData, Base));
        Assert.IsNotType<VisionModuleMissingException>(ex);
        Assert.Contains(mala, ex.Message);

        // Una carpeta propia que existe pero no tiene el exe dentro: el mismo error
        Directory.CreateDirectory(Path.Combine(_raiz, "vacia"));
        var ex2 = Assert.Throws<RastrearUserException>(() =>
            VisionService.FindModule(Path.Combine(_raiz, "vacia"), k => _env.GetValueOrDefault(k), LocalAppData, Base));
        Assert.Contains("vacia", ex2.Message);
    }

    [Fact]
    public void La_variable_de_entorno_puede_apuntar_al_exe_suelto()
    {
        string suelto = Coloca("suelto");
        _env["RASTREAR_MODULOS"] = suelto; // un archivo, no una carpeta
        Assert.Equal(Path.GetFullPath(suelto), Busca());
    }

    [Fact]
    public void En_el_lanzador_solo_cuentan_las_carpetas_de_version()
    {
        string lanzador = Path.Combine(LocalAppData, "Dathinel", "Comun", "modulos", VisionService.ModuleId);
        Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "1.10.0");
        string dos = Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "2.0");
        // El Lanzador extrae en «<versión>.instalando» y la renombra al acabar: si se cortó, ahí hay un módulo a medias
        Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "3.0.0.instalando");
        Coloca("local", "Dathinel", "Comun", "modulos", "modulo-vision", "zzz");
        Assert.Equal(dos, Busca()); // 2.0 > 1.10.0; lo que no es una versión no cuenta

        // Sin ninguna versión entera el módulo falta (y la app manda al Lanzador): no se arranca uno a medio instalar
        Directory.Delete(Path.Combine(lanzador, "1.10.0"), recursive: true);
        Directory.Delete(Path.Combine(lanzador, "2.0"), recursive: true);
        Assert.Throws<VisionModuleMissingException>(Busca);

        // En las demás raíces las subcarpetas son ids de módulo, no versiones, y siguen valiendo
        string rodaje = Coloca("local", "Rodaje", "complementos", "modulos", "modulo-vision");
        Assert.Equal(rodaje, Busca());
    }

    [Fact]
    public void El_modulo_del_repositorio_se_busca_subiendo_desde_el_exe()
    {
        // Base hundida en el repo: sube hasta la raíz que tiene externo\rodaje
        string baseHundida = Path.Combine(_raiz, "repo", "src", "app", "bin");
        Directory.CreateDirectory(baseHundida);
        string debug = Coloca("repo", "externo", "rodaje", "modules", "Rodaje.Module.Vision",
            "bin", "Debug", "net10.0-windows", "win-x64");
        Assert.Equal(Path.GetFullPath(debug),
            VisionService.FindModule(null, _ => null, LocalAppData, baseHundida));

        // Con el publicado Release también presente, ese va primero de los candidatos del repo
        string release = Coloca("repo", "externo", "rodaje", "modules", "Rodaje.Module.Vision",
            "bin", "Release", "net10.0-windows", "win-x64", "publish");
        Assert.Equal(Path.GetFullPath(release),
            VisionService.FindModule(null, _ => null, LocalAppData, baseHundida));
    }

    [Fact]
    public void Sin_modulo_las_rutas_probadas_van_en_orden_y_dos_por_raiz()
    {
        _env["RASTREAR_MODULOS"] = Path.Combine(_raiz, "propios");
        _env["RODAJE_MODULOS"] = Path.Combine(_raiz, "rodaje-propios");
        var ex = Assert.Throws<VisionModuleMissingException>(Busca);

        // Dos entradas por raíz (el exe directo y el comodín), en el orden de precedencia
        Assert.Equal(10, ex.TriedPaths.Count);
        Assert.Equal(Path.Combine(_raiz, "propios", VisionService.ModuleExe), ex.TriedPaths[0]);
        Assert.Equal(Path.Combine(_raiz, "propios", "*", VisionService.ModuleExe), ex.TriedPaths[1]);
        Assert.Equal(Path.Combine(LocalAppData, "Dathinel", "Comun", "modulos", VisionService.ModuleId, VisionService.ModuleExe), ex.TriedPaths[2]);
        Assert.Equal(Path.Combine(LocalAppData, "Rodaje", "complementos", "modulos", VisionService.ModuleExe), ex.TriedPaths[4]);
        Assert.Equal(Path.Combine(_raiz, "rodaje-propios", VisionService.ModuleExe), ex.TriedPaths[6]);
        Assert.Equal(Path.Combine(Base, "modulos", VisionService.ModuleExe), ex.TriedPaths[8]);

        // Las raíces vacías ni se apuntan ni se prueban
        _env["RASTREAR_MODULOS"] = "   ";
        var ex2 = Assert.Throws<VisionModuleMissingException>(Busca);
        Assert.Equal(8, ex2.TriedPaths.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }
}
