using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Rodaje.Modules.Protocol;
using Rastrear.Core.Model;
using Rastrear.Core.Video;

namespace Rastrear.Core.Vision;

[SupportedOSPlatform("windows")]
public static class VisionService
{
    public static string FindModule(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (File.Exists(customPath))
            {
                return Path.GetFullPath(customPath);
            }
            if (Directory.Exists(customPath))
            {
                string inDir = Path.Combine(customPath, "Rodaje.Module.Vision.exe");
                if (File.Exists(inDir))
                {
                    return Path.GetFullPath(inDir);
                }
            }
            throw new RastrearUserException($"No se encontró el módulo de visión en: {customPath}");
        }

        // RASTREAR_MODULOS
        if (Environment.GetEnvironmentVariable("RASTREAR_MODULOS") is { Length: > 0 } envRast)
        {
            if (File.Exists(envRast)) return Path.GetFullPath(envRast);
            string inDir = Path.Combine(envRast, "Rodaje.Module.Vision.exe");
            if (File.Exists(inDir)) return Path.GetFullPath(inDir);
        }

        // RODAJE_MODULO_VISION
        if (Environment.GetEnvironmentVariable("RODAJE_MODULO_VISION") is { Length: > 0 } envMod && File.Exists(envMod))
        {
            return Path.GetFullPath(envMod);
        }

        // Junto a la aplicación
        string[] localCandidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Rodaje.Module.Vision.exe"),
            Path.Combine(AppContext.BaseDirectory, "modulos", "Rodaje.Module.Vision.exe"),
            Path.Combine(AppContext.BaseDirectory, "modulos", "vision", "Rodaje.Module.Vision.exe"),
        ];
        foreach (string c in localCandidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        // En LocalAppData de Rodaje
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDataCandidate = Path.Combine(localAppData, "Rodaje", "complementos", "modulos", "vision", "Rodaje.Module.Vision.exe");
        if (File.Exists(appDataCandidate)) return Path.GetFullPath(appDataCandidate);

        // En el repositorio (desarrollo / pruebas)
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string[] repoCandidates =
            [
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Release", "net10.0-windows", "win-x64", "publish", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Release", "net10.0-windows", "win-x64", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Debug", "net10.0-windows", "win-x64", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Release", "net10.0-windows", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Debug", "net10.0-windows", "Rodaje.Module.Vision.exe"),
            ];
            foreach (string c in repoCandidates)
            {
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
        }

        throw new RastrearUserException(
            "No se encontró el módulo de visión (Rodaje.Module.Vision.exe). Pasa --modulo <ruta> o define la variable RASTREAR_MODULOS o RODAJE_MODULO_VISION.");
    }

    private static void EnsureDotnetEnvironment()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            if (Environment.GetEnvironmentVariable("RODAJE_ENTORNO") is { Length: > 0 } env)
            {
                string dotnetPath = Path.Combine(env, "dotnet");
                if (Directory.Exists(dotnetPath))
                {
                    Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetPath);
                }
            }
            else
            {
                for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, ".entorno", "dotnet");
                    if (Directory.Exists(candidate))
                    {
                        Environment.SetEnvironmentVariable("DOTNET_ROOT", candidate);
                        break;
                    }
                }
            }
        }
    }

    public static async Task<DetectionResult> DetectAsync(
        string videoPath,
        string target = "rostros",
        string device = "auto",
        string? moduleExePath = null,
        string? ffmpegPath = null,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (target is not ("rostros" or "personas"))
        {
            throw new RastrearUserException($"Objetivo de detección inválido: «{target}». Use «rostros» o «personas».");
        }

        string moduleExe = FindModule(moduleExePath);
        string ffmpeg = FfmpegTool.FindFfmpeg(ffmpegPath);
        EnsureDotnetEnvironment();

        VideoInfo info = await FfmpegTool.ProbeAsync(videoPath, ffmpeg, cancellationToken).ConfigureAwait(false);

        ModuleProcess process;
        try
        {
            process = await ModuleProcess.StartAsync(
                moduleExe,
                options: new ModuleClientOptions { App = "rastrear" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RastrearUserException)
        {
            throw new RastrearModuleException($"No se pudo iniciar el proceso del módulo de visión ({moduleExe}): {ex.Message}", ex);
        }

        await using (process.ConfigureAwait(false))
        {
            using Process decoder = FfmpegTool.StartDecoder(videoPath, ffmpeg);
            try
            {
                Stream stdout = decoder.StandardOutput.BaseStream;
                const int BatchSize = 8;
                long frameBytes = info.FrameBytesBgra;

                List<DetectionFrame> allFrames = [];
                List<byte[]> batch = [];
                int frameIdx = 0;

                string computeDevice = device == "cpu" ? "cpu" : "gpu";
                byte[] paramBytes = JsonSerializer.SerializeToUtf8Bytes(
                    new VisionDetectParameters(target, computeDevice),
                    RastrearJsonContext.Default.VisionDetectParameters);
                using JsonDocument paramDoc = JsonDocument.Parse(paramBytes);

                byte[] currentFrame = new byte[frameBytes];
                while (await FfmpegTool.ReadExactBytesAsync(stdout, currentFrame, 0, currentFrame.Length, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(currentFrame);
                    currentFrame = new byte[frameBytes];

                    if (batch.Count == BatchSize)
                    {
                        await ProcessBatchAsync(batch).ConfigureAwait(false);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    await ProcessBatchAsync(batch).ConfigureAwait(false);
                    batch.Clear();
                }

                return new DetectionResult(allFrames);

                async Task ProcessBatchAsync(List<byte[]> framesToProcess)
                {
                    long totalBytes = framesToProcess.Count * frameBytes;
                    SharedMemoryRegion region = process.Client.CreateRegion(totalBytes);
                    List<SharedBufferRef> buffers = new(framesToProcess.Count);

                    for (int i = 0; i < framesToProcess.Count; i++)
                    {
                        long offset = i * frameBytes;
                        framesToProcess[i].CopyTo(region.GetSpan(offset, frameBytes));
                        buffers.Add(region.Describe(offset, frameBytes, new VideoLayout(info.Width, info.Height, info.Width * 4, PixelFormats.Bgra8)));
                    }

                    ModuleResult result = await process.Client.InvokeAsync(
                        "detectar",
                        paramDoc.RootElement.Clone(),
                        buffers: buffers,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (result.Value is { ValueKind: JsonValueKind.Object } v &&
                        v.TryGetProperty("cuadros", out JsonElement cuadrosEl) &&
                        cuadrosEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement frameEl in cuadrosEl.EnumerateArray())
                        {
                            List<DetectionBox> boxes = [];
                            if (frameEl.TryGetProperty("cajas", out JsonElement cajasEl) && cajasEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement boxEl in cajasEl.EnumerateArray())
                                {
                                    double bx = boxEl.GetProperty("x").GetDouble();
                                    double by = boxEl.GetProperty("y").GetDouble();
                                    double bw = boxEl.GetProperty("w").GetDouble();
                                    double bh = boxEl.GetProperty("h").GetDouble();
                                    double bp = boxEl.TryGetProperty("p", out JsonElement pe) ? pe.GetDouble() : 1.0;
                                    boxes.Add(new DetectionBox(bx, by, bw, bh, bp));
                                }
                            }
                            allFrames.Add(new DetectionFrame(boxes));
                        }
                    }

                    frameIdx += framesToProcess.Count;
                    if (info.EstimatedFrames > 0)
                    {
                        progress?.Report(new VideoProgress(Math.Clamp((double)frameIdx / info.EstimatedFrames, 0, 1), "Detectando"));
                    }
                }
            }
            finally
            {
                if (!decoder.HasExited)
                {
                    try { decoder.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
    }

    public static async Task RemoveBackgroundAsync(
        string inputVideo,
        string outputVideo,
        string format = "alfa",
        string quality = "normal",
        string device = "auto",
        string? moduleExePath = null,
        string? ffmpegPath = null,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string moduleExe = FindModule(moduleExePath);
        string ffmpeg = FfmpegTool.FindFfmpeg(ffmpegPath);
        EnsureDotnetEnvironment();

        VideoInfo info = await FfmpegTool.ProbeAsync(inputVideo, ffmpeg, cancellationToken).ConfigureAwait(false);

        // Determinar formato de salida
        OutputVideoFormat outFormat;
        if (format.Equals("mascara", StringComparison.OrdinalIgnoreCase))
        {
            outFormat = OutputVideoFormat.GrayMp4;
        }
        else if (outputVideo.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
        {
            outFormat = OutputVideoFormat.ProRes4444Mov;
        }
        else
        {
            outFormat = OutputVideoFormat.Vp9AlphaWebm;
        }

        string? outDir = Path.GetDirectoryName(outputVideo);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        ModuleProcess process;
        try
        {
            process = await ModuleProcess.StartAsync(
                moduleExe,
                options: new ModuleClientOptions { App = "rastrear" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RastrearUserException)
        {
            throw new RastrearModuleException($"No se pudo iniciar el proceso del módulo de visión ({moduleExe}): {ex.Message}", ex);
        }

        await using (process.ConfigureAwait(false))
        {
            using Process decoder = FfmpegTool.StartDecoder(inputVideo, ffmpeg);
            using Process encoder = FfmpegTool.StartEncoder(outputVideo, info.Width, info.Height, info.Fps, outFormat, ffmpeg);

            try
            {
                Stream decodeStream = decoder.StandardOutput.BaseStream;
                Stream encodeStream = encoder.StandardInput.BaseStream;

                const int BatchSize = 4;
                long inputBytes = info.FrameBytesBgra;
                long maskBytes = (long)info.Width * info.Height;
                int pixelCount = info.Width * info.Height;

                string computeDevice = device == "cpu" ? "cpu" : "gpu";
                string qual = quality == "alta" ? "alta" : "normal";

                List<byte[]> batch = [];
                int frameIdx = 0;
                bool isFirstBatch = true;

                byte[] currentFrame = new byte[inputBytes];
                while (await FfmpegTool.ReadExactBytesAsync(decodeStream, currentFrame, 0, currentFrame.Length, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(currentFrame);
                    currentFrame = new byte[inputBytes];

                    if (batch.Count == BatchSize)
                    {
                        await ProcessMatteBatchAsync(batch, isFirstBatch).ConfigureAwait(false);
                        isFirstBatch = false;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    await ProcessMatteBatchAsync(batch, isFirstBatch).ConfigureAwait(false);
                    batch.Clear();
                }

                await encodeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                encoder.StandardInput.Close();

                await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (encoder.ExitCode != 0)
                {
                    string err = await encoder.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    throw new RastrearModuleException($"FFmpeg falló al codificar el video ({encoder.ExitCode}): {err}");
                }

                async Task ProcessMatteBatchAsync(List<byte[]> framesToProcess, bool reiniciar)
                {
                    long totalBytes = framesToProcess.Count * (inputBytes + maskBytes);
                    SharedMemoryRegion region = process.Client.CreateRegion(totalBytes);
                    List<SharedBufferRef> buffers = new(framesToProcess.Count * 2);

                    long offset = 0;
                    for (int i = 0; i < framesToProcess.Count; i++)
                    {
                        framesToProcess[i].CopyTo(region.GetSpan(offset, inputBytes));
                        buffers.Add(region.Describe(offset, inputBytes, new VideoLayout(info.Width, info.Height, info.Width * 4, PixelFormats.Bgra8)));
                        offset += inputBytes;

                        buffers.Add(region.Describe(offset, maskBytes, new VideoLayout(info.Width, info.Height, info.Width, PixelFormats.Gray8)));
                        offset += maskBytes;
                    }

                    byte[] paramBytes = JsonSerializer.SerializeToUtf8Bytes(
                        new MattingParameters(qual, "rastrear-bg", reiniciar, computeDevice),
                        RastrearJsonContext.Default.MattingParameters);
                    using JsonDocument paramDoc = JsonDocument.Parse(paramBytes);

                    await process.Client.InvokeAsync(
                        "quitar-fondo",
                        paramDoc.RootElement.Clone(),
                        buffers: buffers,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    // Escribir cuadros resultantes
                    byte[] outFrame = new byte[inputBytes];
                    for (int i = 0; i < framesToProcess.Count; i++)
                    {
                        SharedBufferRef maskRef = buffers[(i * 2) + 1];
                        ReadOnlySpan<byte> maskSpan = region.GetSpan(maskRef);
                        byte[] inFrame = framesToProcess[i];

                        if (outFormat == OutputVideoFormat.GrayMp4)
                        {
                            // Video gris de la máscara
                            for (int p = 0; p < pixelCount; p++)
                            {
                                byte m = maskSpan[p];
                                int o = p * 4;
                                outFrame[o] = m;
                                outFrame[o + 1] = m;
                                outFrame[o + 2] = m;
                                outFrame[o + 3] = 255;
                            }
                            await encodeStream.WriteAsync(outFrame, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            // Video con canal alfa asignado en el byte A de BGRA
                            for (int p = 0; p < pixelCount; p++)
                            {
                                inFrame[(p * 4) + 3] = maskSpan[p];
                            }
                            await encodeStream.WriteAsync(inFrame, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    frameIdx += framesToProcess.Count;
                    if (info.EstimatedFrames > 0)
                    {
                        progress?.Report(new VideoProgress(Math.Clamp((double)frameIdx / info.EstimatedFrames, 0, 1), "Quitando fondo"));
                    }
                }
            }
            finally
            {
                if (!decoder.HasExited)
                {
                    try { decoder.Kill(entireProcessTree: true); } catch { }
                }
                if (!encoder.HasExited)
                {
                    try { encoder.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
    }

    public static async Task<ReframeOutput> ReframeAsync(
        string inputVideo,
        string outputVideo,
        double targetAspect = 9.0 / 16.0,
        double smoothing = 0.5,
        string device = "auto",
        string? moduleExePath = null,
        string? ffmpegPath = null,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string moduleExe = FindModule(moduleExePath);
        string ffmpeg = FfmpegTool.FindFfmpeg(ffmpegPath);
        EnsureDotnetEnvironment();

        VideoInfo info = await FfmpegTool.ProbeAsync(inputVideo, ffmpeg, cancellationToken).ConfigureAwait(false);

        // Fase 1: Detectar rostros cuadro a cuadro para alimentar el reencuadre
        DetectionResult detections = await DetectAsync(
            inputVideo,
            target: "rostros",
            device: device,
            moduleExePath: moduleExe,
            ffmpegPath: ffmpeg,
            progress: progress,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        List<ReframeInputFrame> reframeFrames = new(detections.Cuadros.Count);
        for (int i = 0; i < detections.Cuadros.Count; i++)
        {
            double t = i / info.Fps;
            reframeFrames.Add(new ReframeInputFrame(t, detections.Cuadros[i].Cajas));
        }

        // Llamar a reencuadre en el módulo
        ModuleProcess process;
        try
        {
            process = await ModuleProcess.StartAsync(
                moduleExe,
                options: new ModuleClientOptions { App = "rastrear" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RastrearUserException)
        {
            throw new RastrearModuleException($"No se pudo iniciar el proceso del módulo de visión ({moduleExe}): {ex.Message}", ex);
        }

        ReframeOutput reframeOutput;
        await using (process.ConfigureAwait(false))
        {
            ReframeParameters parameters = new(
                Aspecto: targetAspect,
                Fuente: new ReframeSource(info.Width, info.Height),
                Cuadros: reframeFrames,
                Suavizado: smoothing);

            byte[] paramBytes = JsonSerializer.SerializeToUtf8Bytes(parameters, RastrearJsonContext.Default.ReframeParameters);
            using JsonDocument paramDoc = JsonDocument.Parse(paramBytes);

            ModuleResult result = await process.Client.InvokeAsync(
                "reencuadre",
                paramDoc.RootElement.Clone(),
                buffers: [],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Value is not { ValueKind: JsonValueKind.Object } v)
            {
                throw new RastrearModuleException("La respuesta de «reencuadre» no es un objeto válido.");
            }

            List<ReframeKeyframe> keyframes = [];
            if (v.TryGetProperty("keyframes", out JsonElement keysEl) && keysEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement k in keysEl.EnumerateArray())
                {
                    double kt = k.GetProperty("t").GetDouble();
                    double kx = k.GetProperty("x").GetDouble();
                    double ky = k.GetProperty("y").GetDouble();
                    double ks = k.GetProperty("scale").GetDouble();
                    keyframes.Add(new ReframeKeyframe(kt, kx, ky, ks));
                }
            }

            double dentro = v.TryGetProperty("dentro", out JsonElement dEl) ? dEl.GetDouble() : 1.0;
            reframeOutput = new ReframeOutput(keyframes, dentro);
        }

        // Fase 2: Recortar y codificar el video resultante
        double w = info.Width, h = info.Height;
        (double rawCropW, double rawCropH) = (w / h > targetAspect)
            ? (targetAspect * h, h)
            : (w, w / targetAspect);

        int cropW = ((int)Math.Round(rawCropW)) & ~1;
        int cropH = ((int)Math.Round(rawCropH)) & ~1;
        cropW = Math.Clamp(cropW, 2, info.Width & ~1);
        cropH = Math.Clamp(cropH, 2, info.Height & ~1);

        string? outDir = Path.GetDirectoryName(outputVideo);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using Process pass2Decoder = FfmpegTool.StartDecoder(inputVideo, ffmpeg);
        using Process encoder = FfmpegTool.StartEncoder(outputVideo, cropW, cropH, info.Fps, OutputVideoFormat.H264Mp4, ffmpeg);

        try
        {
            Stream decodeStream = pass2Decoder.StandardOutput.BaseStream;
            Stream encodeStream = encoder.StandardInput.BaseStream;

            byte[] bgraFrame = new byte[info.FrameBytesBgra];
            byte[] croppedBgra = new byte[cropW * cropH * 4];

            int frameIdx = 0;
            int total = detections.Cuadros.Count;

            while (await FfmpegTool.ReadExactBytesAsync(decodeStream, bgraFrame, 0, bgraFrame.Length, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                double t = frameIdx / info.Fps;

                (double cxNorm, double cyNorm) = InterpolateKeyframe(reframeOutput.Keyframes, t);
                double cx = cxNorm * info.Width;
                double cy = cyNorm * info.Height;

                int left = Math.Clamp((int)Math.Round(cx - (cropW / 2.0)), 0, info.Width - cropW);
                int top = Math.Clamp((int)Math.Round(cy - (cropH / 2.0)), 0, info.Height - cropH);

                for (int y = 0; y < cropH; y++)
                {
                    int srcOffset = (((top + y) * info.Width) + left) * 4;
                    int dstOffset = y * cropW * 4;
                    Buffer.BlockCopy(bgraFrame, srcOffset, croppedBgra, dstOffset, cropW * 4);
                }

                await encodeStream.WriteAsync(croppedBgra, cancellationToken).ConfigureAwait(false);

                frameIdx++;
                if (total > 0)
                {
                    progress?.Report(new VideoProgress(Math.Clamp((double)frameIdx / total, 0, 1), "Reencuadrando"));
                }
            }

            await encodeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            encoder.StandardInput.Close();

            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (encoder.ExitCode != 0)
            {
                string err = await encoder.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                throw new RastrearModuleException($"FFmpeg falló al codificar el video reencuadrado ({encoder.ExitCode}): {err}");
            }

            return reframeOutput;
        }
        finally
        {
            if (!pass2Decoder.HasExited)
            {
                try { pass2Decoder.Kill(entireProcessTree: true); } catch { }
            }
            if (!encoder.HasExited)
            {
                try { encoder.Kill(entireProcessTree: true); } catch { }
            }
        }
    }

    public static (double X, double Y) InterpolateKeyframe(IReadOnlyList<ReframeKeyframe> keys, double t)
    {
        if (keys.Count == 0) return (0.5, 0.5);
        if (t <= keys[0].T || keys.Count == 1) return (keys[0].X, keys[0].Y);

        for (int k = 1; k < keys.Count; k++)
        {
            if (t <= keys[k].T)
            {
                double span = keys[k].T - keys[k - 1].T;
                if (span <= 1e-9) return (keys[k].X, keys[k].Y);
                double f = (t - keys[k - 1].T) / span;
                return (
                    keys[k - 1].X + ((keys[k].X - keys[k - 1].X) * f),
                    keys[k - 1].Y + ((keys[k].Y - keys[k - 1].Y) * f));
            }
        }

        return (keys[^1].X, keys[^1].Y);
    }
}
