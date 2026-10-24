using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Rodaje.Analysis.Video;
using Rodaje.Modules.Protocol;
using Rastrear.Core;
using Rastrear.Core.Model;
using Rastrear.Core.Stabilization;
using Rastrear.Core.Tracking;
using Rastrear.Core.Video;
using Rastrear.Core.Vision;

namespace Rastrear.Cli;

[SupportedOSPlatform("windows")]
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] cmdArgs = args[1..];

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine();
            Console.Error.WriteLine("Cancelando a petición del usuario...");
            cts.Cancel();
        };

        try
        {
            return command switch
            {
                "seguir" => await RunSeguirAsync(cmdArgs, cts.Token).ConfigureAwait(false),
                "quitar-fondo" => await RunQuitarFondoAsync(cmdArgs, cts.Token).ConfigureAwait(false),
                "detectar" => await RunDetectarAsync(cmdArgs, cts.Token).ConfigureAwait(false),
                "reencuadrar" => await RunReencuadrarAsync(cmdArgs, cts.Token).ConfigureAwait(false),
                "estabilizar" => await RunEstabilizarAsync(cmdArgs, cts.Token).ConfigureAwait(false),
                _ => HandleUnknownCommand(command),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Operación cancelada.");
            return 1;
        }
        catch (RastrearUserException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (RastrearModuleException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error del sistema: {ex.Message}");
            return 2;
        }
        catch (ModuleException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error del módulo: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error inesperado: {ex.Message}");
            return 2;
        }
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: «{command}».");
        PrintUsage();
        return 1;
    }

    private static Progress<VideoProgress> CreateProgress()
    {
        string lastStage = "";
        int lastPercent = -1;
        return new Progress<VideoProgress>(p =>
        {
            int percent = (int)(p.Fraction * 100);
            if (p.Stage != lastStage || percent != lastPercent)
            {
                lastStage = p.Stage ?? "";
                lastPercent = percent;
                Console.Error.Write($"\rProgreso: {percent}% ({lastStage})    ");
            }
        });
    }

    private static async Task<int> RunSeguirAsync(string[] args, CancellationToken ct)
    {
        string? inputFile = null;
        string? salida = null;
        string? formato = null;
        string? ffmpegPath = null;
        PointTargetSpec? point = null;
        RegionTargetSpec? region = null;
        int patternSize = 41;
        int searchSize = 121;
        float threshold = 0.7f;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if ((a.Equals("--punto", StringComparison.OrdinalIgnoreCase) || a.Equals("-p", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                string[] parts = args[++i].Split(',');
                if (parts.Length != 2 || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out double px) || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double py))
                {
                    throw new RastrearUserException("Coordenadas de --punto inválidas. Formato esperado: x,y");
                }
                point = new PointTargetSpec(px, py);
            }
            else if ((a.Equals("--region", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                string[] parts = args[++i].Split(',');
                if (parts.Length != 4 ||
                    !double.TryParse(parts[0], CultureInfo.InvariantCulture, out double rx) ||
                    !double.TryParse(parts[1], CultureInfo.InvariantCulture, out double ry) ||
                    !double.TryParse(parts[2], CultureInfo.InvariantCulture, out double rw) ||
                    !double.TryParse(parts[3], CultureInfo.InvariantCulture, out double rh))
                {
                    throw new RastrearUserException("Coordenadas de --region inválidas. Formato esperado: x,y,w,h");
                }
                region = new RegionTargetSpec(rx, ry, rw, rh);
            }
            else if (a.Equals("--formato", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                formato = args[++i].ToLowerInvariant();
            }
            else if ((a.Equals("--salida", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                salida = args[++i];
            }
            else if (a.Equals("--patron", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out int pat))
            {
                patternSize = pat;
            }
            else if (a.Equals("--busqueda", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out int sea))
            {
                searchSize = sea;
            }
            else if (a.Equals("--umbral", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && float.TryParse(args[++i], CultureInfo.InvariantCulture, out float umb))
            {
                threshold = umb;
            }
            else if (a.Equals("--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ffmpegPath = args[++i];
            }
            else if (!a.StartsWith('-') && inputFile is null)
            {
                inputFile = a;
            }
            else
            {
                throw new RastrearUserException($"Opción no válida para «seguir»: {a}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile))
        {
            throw new RastrearUserException("Debe especificar un archivo de video de entrada.");
        }

        if (point is null && region is null)
        {
            throw new RastrearUserException("Debe especificar --punto x,y o --region x,y,w,h para seguir.");
        }

        if (string.IsNullOrEmpty(formato))
        {
            if (!string.IsNullOrEmpty(salida) && salida.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                formato = "json";
            }
            else
            {
                formato = "csv";
            }
        }

        if (formato is not ("csv" or "json"))
        {
            throw new RastrearUserException($"Formato no admitido: «{formato}». Use «csv» o «json».");
        }

        if (string.IsNullOrWhiteSpace(salida))
        {
            string ext = "." + formato;
            string? dir = Path.GetDirectoryName(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);
            salida = string.IsNullOrEmpty(dir) ? $"{baseName}-seguimiento{ext}" : Path.Combine(dir, $"{baseName}-seguimiento{ext}");
        }

        Console.Error.WriteLine($"Siguiendo objetivo en «{Path.GetFileName(inputFile)}»...");
        FollowResult result = await FollowService.FollowAsync(
            inputFile,
            point,
            region,
            patternSize,
            searchSize,
            threshold,
            ffmpegPath,
            CreateProgress(),
            ct).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Seguimiento completado ({result.Cuadros.Count} cuadros). Guardando resultados...");

        if (salida == "-")
        {
            if (formato == "json")
            {
                FollowService.WriteJson(result, Console.Out);
            }
            else
            {
                FollowService.WriteCsv(result, Console.Out);
            }
        }
        else
        {
            string? outDir = Path.GetDirectoryName(salida);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            using StreamWriter writer = new(salida, false, System.Text.Encoding.UTF8);
            if (formato == "json")
            {
                FollowService.WriteJson(result, writer);
            }
            else
            {
                FollowService.WriteCsv(result, writer);
            }
            Console.Error.WriteLine($"Resultado guardado en: {salida}");
        }

        return 0;
    }

    private static async Task<int> RunQuitarFondoAsync(string[] args, CancellationToken ct)
    {
        string? inputFile = null;
        string? salida = null;
        string formato = "alfa";
        string calidad = "normal";
        string device = "auto";
        string? moduloPath = null;
        string? ffmpegPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--formato", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                formato = args[++i].ToLowerInvariant();
            }
            else if (a.Equals("--mascara", StringComparison.OrdinalIgnoreCase))
            {
                formato = "mascara";
            }
            else if (a.Equals("--calidad", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                calidad = args[++i].ToLowerInvariant();
            }
            else if ((a.Equals("--salida", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                salida = args[++i];
            }
            else if (a.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                device = args[++i].ToLowerInvariant();
            }
            else if (a.Equals("--modulo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                moduloPath = args[++i];
            }
            else if (a.Equals("--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ffmpegPath = args[++i];
            }
            else if (!a.StartsWith('-') && inputFile is null)
            {
                inputFile = a;
            }
            else
            {
                throw new RastrearUserException($"Opción no válida para «quitar-fondo»: {a}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile))
        {
            throw new RastrearUserException("Debe especificar un archivo de video de entrada.");
        }

        if (string.IsNullOrWhiteSpace(salida))
        {
            string ext = formato == "mascara" ? "-mascara.mp4" : "-fondo.webm";
            string? dir = Path.GetDirectoryName(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);
            salida = string.IsNullOrEmpty(dir) ? $"{baseName}{ext}" : Path.Combine(dir, $"{baseName}{ext}");
        }

        Console.Error.WriteLine($"Quitando fondo de «{Path.GetFileName(inputFile)}» ({formato}, calidad {calidad})...");
        await VisionService.RemoveBackgroundAsync(
            inputFile,
            salida,
            formato,
            calidad,
            device,
            moduloPath,
            ffmpegPath,
            CreateProgress(),
            ct).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Video generado con éxito: {salida}");
        return 0;
    }

    private static async Task<int> RunDetectarAsync(string[] args, CancellationToken ct)
    {
        string? inputFile = null;
        string? salida = null;
        string objetivo = "rostros";
        string device = "auto";
        string? moduloPath = null;
        string? ffmpegPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--objetivo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                objetivo = args[++i].ToLowerInvariant();
            }
            else if ((a.Equals("--salida", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                salida = args[++i];
            }
            else if (a.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                device = args[++i].ToLowerInvariant();
            }
            else if (a.Equals("--modulo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                moduloPath = args[++i];
            }
            else if (a.Equals("--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ffmpegPath = args[++i];
            }
            else if (!a.StartsWith('-') && inputFile is null)
            {
                inputFile = a;
            }
            else
            {
                throw new RastrearUserException($"Opción no válida para «detectar»: {a}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile))
        {
            throw new RastrearUserException("Debe especificar un archivo de video de entrada.");
        }

        if (string.IsNullOrWhiteSpace(salida))
        {
            string? dir = Path.GetDirectoryName(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);
            salida = string.IsNullOrEmpty(dir) ? $"{baseName}-detecciones.json" : Path.Combine(dir, $"{baseName}-detecciones.json");
        }

        Console.Error.WriteLine($"Detectando {objetivo} en «{Path.GetFileName(inputFile)}»...");
        DetectionResult result = await VisionService.DetectAsync(
            inputFile,
            objetivo,
            device,
            moduloPath,
            ffmpegPath,
            CreateProgress(),
            ct).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Detección completada ({result.Cuadros.Count} cuadros). Guardando JSON...");

        string json = JsonSerializer.Serialize(result, RastrearJsonContext.Default.DetectionResult);

        if (salida == "-")
        {
            Console.Out.Write(json);
        }
        else
        {
            string? outDir = Path.GetDirectoryName(salida);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            await File.WriteAllTextAsync(salida, json, ct).ConfigureAwait(false);
            Console.Error.WriteLine($"Detecciones guardadas en: {salida}");
        }

        return 0;
    }

    private static async Task<int> RunReencuadrarAsync(string[] args, CancellationToken ct)
    {
        string? inputFile = null;
        string? salida = null;
        double targetAspect = 9.0 / 16.0;
        double smoothing = 0.5;
        string device = "auto";
        string? moduloPath = null;
        string? ffmpegPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if ((a.Equals("--a", StringComparison.OrdinalIgnoreCase) || a.Equals("--aspecto", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                string val = args[++i];
                if (val.Contains(':'))
                {
                    string[] parts = val.Split(':');
                    if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out double w) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out double h) && h > 0)
                    {
                        targetAspect = w / h;
                    }
                    else
                    {
                        throw new RastrearUserException($"Relación de aspecto inválida: «{val}». Use por ejemplo 9:16 o 1:1");
                    }
                }
                else if (double.TryParse(val, CultureInfo.InvariantCulture, out double num) && num > 0)
                {
                    targetAspect = num;
                }
                else
                {
                    throw new RastrearUserException($"Relación de aspecto inválida: «{val}».");
                }
            }
            else if (a.Equals("--suavizado", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && double.TryParse(args[++i], CultureInfo.InvariantCulture, out double s))
            {
                smoothing = s;
            }
            else if ((a.Equals("--salida", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                salida = args[++i];
            }
            else if (a.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                device = args[++i].ToLowerInvariant();
            }
            else if (a.Equals("--modulo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                moduloPath = args[++i];
            }
            else if (a.Equals("--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ffmpegPath = args[++i];
            }
            else if (!a.StartsWith('-') && inputFile is null)
            {
                inputFile = a;
            }
            else
            {
                throw new RastrearUserException($"Opción no válida para «reencuadrar»: {a}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile))
        {
            throw new RastrearUserException("Debe especificar un archivo de video de entrada.");
        }

        if (string.IsNullOrWhiteSpace(salida))
        {
            string? dir = Path.GetDirectoryName(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);
            salida = string.IsNullOrEmpty(dir) ? $"{baseName}-reencuadrado.mp4" : Path.Combine(dir, $"{baseName}-reencuadrado.mp4");
        }

        Console.Error.WriteLine($"Reencuadrando «{Path.GetFileName(inputFile)}» a relación {targetAspect:0.###}...");
        ReframeOutput output = await VisionService.ReframeAsync(
            inputFile,
            salida,
            targetAspect,
            smoothing,
            device,
            moduloPath,
            ffmpegPath,
            CreateProgress(),
            ct).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Video reencuadrado generado con éxito: {salida} (sujeto dentro en {output.Dentro:P1} de cuadros)");
        return 0;
    }

    private static async Task<int> RunEstabilizarAsync(string[] args, CancellationToken ct)
    {
        string? inputFile = null;
        string? salida = null;
        double intensity = 0.5;
        bool autoCrop = true;
        string? ffmpegPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--intensidad", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && double.TryParse(args[++i], CultureInfo.InvariantCulture, out double inten))
            {
                intensity = inten;
            }
            else if (a.Equals("--sin-recorte", StringComparison.OrdinalIgnoreCase))
            {
                autoCrop = false;
            }
            else if (a.Equals("--recorte", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && bool.TryParse(args[++i], out bool crop))
            {
                autoCrop = crop;
            }
            else if ((a.Equals("--salida", StringComparison.OrdinalIgnoreCase) || a.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                salida = args[++i];
            }
            else if (a.Equals("--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                ffmpegPath = args[++i];
            }
            else if (!a.StartsWith('-') && inputFile is null)
            {
                inputFile = a;
            }
            else
            {
                throw new RastrearUserException($"Opción no válida para «estabilizar»: {a}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile))
        {
            throw new RastrearUserException("Debe especificar un archivo de video de entrada.");
        }

        if (string.IsNullOrWhiteSpace(salida))
        {
            string? dir = Path.GetDirectoryName(inputFile);
            string baseName = Path.GetFileNameWithoutExtension(inputFile);
            salida = string.IsNullOrEmpty(dir) ? $"{baseName}-estabilizado.mp4" : Path.Combine(dir, $"{baseName}-estabilizado.mp4");
        }

        Console.Error.WriteLine($"Estabilizando «{Path.GetFileName(inputFile)}» (intensidad {intensity:0.##}, recorte automático: {autoCrop})...");
        StabilizationResult result = await StabilizeService.StabilizeAsync(
            inputFile,
            salida,
            intensity,
            autoCrop,
            ffmpegPath,
            CreateProgress(),
            ct).ConfigureAwait(false);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Video estabilizado generado con éxito: {salida}");
        Console.Error.WriteLine($"Temblor reducido de {result.ShakeBefore:0.###} a {result.ShakeAfter:0.###} (zoom ×{result.Zoom:0.###})");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Uso: Rastrear.Cli <comando> <video> [opciones]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Comandos:");
        Console.Error.WriteLine("  seguir <video>        Sigue un punto o región y genera CSV o JSON de trayectorias");
        Console.Error.WriteLine("  quitar-fondo <video>  Genera video con alfa (VP9 WebM/ProRes) o máscara en escala de grises");
        Console.Error.WriteLine("  detectar <video>      Detecta rostros o personas y genera JSON de cuadros y cajas");
        Console.Error.WriteLine("  reencuadrar <video>   Recorte inteligente que sigue al sujeto (por defecto 9:16)");
        Console.Error.WriteLine("  estabilizar <video>   Estabiliza el movimiento de cámara mediante suavizado de trayectoria");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Opciones comunes:");
        Console.Error.WriteLine("  --salida, -o <ruta>   Ruta de archivo de salida (o «-» para salida estándar en datos)");
        Console.Error.WriteLine("  --ffmpeg <ruta>       Ruta alternativa al ejecutable ffmpeg.exe");
        Console.Error.WriteLine("  --modulo <ruta>       Ruta alternativa a Rodaje.Module.Vision.exe");
        Console.Error.WriteLine("  --device <auto|gpu|cpu> Dispositivo de ejecución para inferencia de visión");
        Console.Error.WriteLine("  --help, -h            Muestra esta ayuda");
    }
}
