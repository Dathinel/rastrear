using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Rastrear.App;

internal static class Program
{
    public static double StartupMs { get; private set; }
    public static bool MeasureStartup { get; private set; }
    public static App? Instance { get; private set; }
    public static string? VideoArg { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        DateTime processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        MeasureStartup = args.Any(a => a.Equals("--medir-arranque", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("-m", StringComparison.OrdinalIgnoreCase));

        VideoArg = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(_ =>
        {
            // Sin esto, cada await de la ventana sigue en otro hilo y tocar la interfaz falla (RPC_E_WRONG_THREAD).
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            StartupMs = (DateTime.UtcNow - processStart).TotalMilliseconds;
            Instance = new App();
        });

        return 0;
    }
}
