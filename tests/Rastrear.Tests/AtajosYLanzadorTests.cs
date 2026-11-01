using Rastrear.Core.Ui;
using Rastrear.Core.Vision;

namespace Rastrear.Tests;

public sealed class AtajosYLanzadorTests : TemporaryFolderTest
{
    [Fact]
    public void Atajos_solo_cuando_tienen_sentido()
    {
        Assert.Equal(ShortcutAction.OpenVideo, KeyboardShortcuts.Resolve("O", ctrl: true, running: false, hasVideo: false));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("O", ctrl: true, running: true, hasVideo: true));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("O", ctrl: false, running: false, hasVideo: false));

        Assert.Equal(ShortcutAction.Run, KeyboardShortcuts.Resolve("Enter", ctrl: true, running: false, hasVideo: true));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("Enter", ctrl: true, running: false, hasVideo: false));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("Enter", ctrl: true, running: true, hasVideo: true));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("Enter", ctrl: false, running: false, hasVideo: true));

        Assert.Equal(ShortcutAction.Cancel, KeyboardShortcuts.Resolve("Escape", ctrl: false, running: true, hasVideo: true));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("Escape", ctrl: false, running: false, hasVideo: true));
        Assert.Equal(ShortcutAction.None, KeyboardShortcuts.Resolve("", ctrl: true, running: false, hasVideo: true));
    }

    [Fact]
    public void Lanzador_por_variable_o_en_LOCALAPPDATA_y_mensaje_claro_si_falta_el_modulo()
    {
        Dictionary<string, string?> env = [];
        string local = PathOf("local");
        Assert.Null(LauncherLocator.Find(k => env.GetValueOrDefault(k), local));

        string enApp = Touch("local", "Dathinel", "Lanzador", "app", LauncherLocator.ExeName);
        Assert.Equal(enApp, LauncherLocator.Find(k => env.GetValueOrDefault(k), local));

        string directo = Touch("local", "Dathinel", "Lanzador", LauncherLocator.ExeName);
        Assert.Equal(directo, LauncherLocator.Find(k => env.GetValueOrDefault(k), local));

        // Donde lo deja de verdad el instalador del Lanzador ({userpf}\Lanzador) gana a las suposiciones
        string instalado = Touch("local", "Programs", "Lanzador", LauncherLocator.ExeName);
        Assert.Equal(instalado, LauncherLocator.Find(k => env.GetValueOrDefault(k), local));

        string propio = PathOf("propio");
        Touch("propio", LauncherLocator.ExeName);
        env["RASTREAR_LANZADOR"] = propio;
        Assert.Equal(Path.Combine(propio, LauncherLocator.ExeName), LauncherLocator.Find(k => env.GetValueOrDefault(k), local));

        VisionModuleMissingException ex = new([PathOf("x", VisionService.ModuleExe)]);
        string con = VisionModuleMissingException.UserMessage(launcherFound: true);
        string sin = VisionModuleMissingException.UserMessage(launcherFound: false);
        Assert.Contains("modulo-vision", con);
        Assert.Contains("Lanzador", con);
        Assert.DoesNotContain(Root, con); // Sin rutas: las rutas van en ex.Message, para la Cli
        Assert.Contains("RASTREAR_MODULOS", sin);
        Assert.Contains("Rutas probadas", ex.Message);
    }
}
