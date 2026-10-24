using System.Diagnostics;
using System.Text.Json;
using Rodaje.Modules.Protocol;
using Rastrear.Core.Model;
using Rastrear.Core.Video;
using Rastrear.Core.Vision;
using Xunit.Abstractions;

namespace Rastrear.Tests;

public sealed class VisionTests(ITestOutputHelper output)
{
    private static bool RunLongTests =>
        TestMediaHelper.IsLongTestsEnabled() && TestMediaHelper.HasVisionModels();

    [Fact]
    public async Task Quitar_fondo_mask_iou_exceeds_90_percent()
    {
        if (!RunLongTests)
        {
            output.WriteLine("Omitida: faltan los modelos de visión o no está activado RODAJE_PRUEBAS_LARGAS=1.");
            return;
        }

        string? env = TestMediaHelper.FindEnvironment();
        Assert.NotNull(env);
        string portraitPath = Path.Combine(env, "corpus", "vision", "retrato.png");
        if (!File.Exists(portraitPath))
        {
            output.WriteLine($"Omitida: no se encontró {portraitPath}");
            return;
        }

        string visionExe = TestMediaHelper.FindVisionModule();
        string ffmpeg = TestMediaHelper.FindFfmpeg();

        // Cargar retrato.png con FFmpeg a BGRA crudo
        int width = 512, height = 512;
        byte[] rawPortrait = new byte[width * height * 4];
        using (var decoder = FfmpegTool.StartDecoder(portraitPath, ffmpeg))
        {
            await FfmpegTool.ReadExactBytesAsync(decoder.StandardOutput.BaseStream, rawPortrait, 0, rawPortrait.Length, CancellationToken.None);
        }

        // Separar la alfa verdadera (verdad del recorte) y componer sobre un fondo sólido
        byte[] truthMask = new byte[width * height];
        byte[] composedBgra = new byte[width * height * 4];

        for (int i = 0; i < width * height; i++)
        {
            int o = i * 4;
            byte b = rawPortrait[o];
            byte g = rawPortrait[o + 1];
            byte r = rawPortrait[o + 2];
            byte a = rawPortrait[o + 3];

            truthMask[i] = a;

            // Composición sobre fondo gris claro (180, 180, 180)
            double alpha = a / 255.0;
            composedBgra[o] = (byte)Math.Clamp((int)((b * alpha) + (180 * (1.0 - alpha))), 0, 255);
            composedBgra[o + 1] = (byte)Math.Clamp((int)((g * alpha) + (180 * (1.0 - alpha))), 0, 255);
            composedBgra[o + 2] = (byte)Math.Clamp((int)((r * alpha) + (180 * (1.0 - alpha))), 0, 255);
            composedBgra[o + 3] = 255;
        }

        // Llamar a quitar-fondo en el módulo Vision por el protocolo
        await using ModuleProcess module = await ModuleProcess.StartAsync(visionExe);
        long inBytes = (long)width * height * 4;
        long outBytes = (long)width * height;

        SharedMemoryRegion region = module.Client.CreateRegion(inBytes + outBytes);
        composedBgra.CopyTo(region.GetSpan(0, inBytes));

        SharedBufferRef inputBuffer = region.Describe(0, inBytes, new VideoLayout(width, height, width * 4, PixelFormats.Bgra8));
        SharedBufferRef outputBuffer = region.Describe(inBytes, outBytes, new VideoLayout(width, height, width, PixelFormats.Gray8));

        using JsonDocument paramDoc = JsonDocument.Parse("""{"sesion":"test-iou","reiniciar":true,"dispositivo":"cpu"}""");
        ModuleResult result = await module.Client.InvokeAsync(
            "quitar-fondo",
            paramDoc.RootElement.Clone(),
            buffers: [inputBuffer, outputBuffer]);

        ReadOnlySpan<byte> predictedMask = region.GetSpan(outputBuffer);
        double iou = TestMediaHelper.ComputeIou(predictedMask, truthMask);

        output.WriteLine($"quitar-fondo IoU sobre imagen de referencia (Lewis Blanton): {iou:F4}");
        Assert.True(iou > 0.90, $"El IoU de la máscara ({iou:F4}) debe ser mayor a 0.90.");
    }

    [Fact]
    public async Task Detectar_faces_and_persons_with_real_module()
    {
        if (!RunLongTests)
        {
            output.WriteLine("Omitida: faltan los modelos de visión o no está activado RODAJE_PRUEBAS_LARGAS=1.");
            return;
        }

        string? env = TestMediaHelper.FindEnvironment();
        Assert.NotNull(env);
        string portraitPath = Path.Combine(env, "corpus", "vision", "retrato.png");
        if (!File.Exists(portraitPath))
        {
            output.WriteLine($"Omitida: no se encontró {portraitPath}");
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "RastrearTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string videoPath = Path.Combine(tempDir, "clip_rostro.mp4");

        try
        {
            string ffmpeg = TestMediaHelper.FindFfmpeg();
            // Crear clip corto de 3 cuadros con retrato.png
            using (var enc = Process.Start(new ProcessStartInfo(ffmpeg,
                $"-hide_banner -loglevel error -y -loop 1 -i \"{portraitPath}\" -c:v h264_mf -pix_fmt yuv420p -t 0.1 \"{videoPath}\"")
            { UseShellExecute = false, CreateNoWindow = true }))
            {
                await enc!.WaitForExitAsync();
            }

            string visionExe = TestMediaHelper.FindVisionModule();
            DetectionResult faces = await VisionService.DetectAsync(
                videoPath,
                target: "rostros",
                device: "cpu",
                moduleExePath: visionExe);

            output.WriteLine($"Detección de rostros: {faces.Cuadros.Count} cuadros analizados.");
            Assert.NotEmpty(faces.Cuadros);
            Assert.NotEmpty(faces.Cuadros[0].Cajas);

            DetectionBox box = faces.Cuadros[0].Cajas[0];
            output.WriteLine($"Rostro detectado en: x={box.X:F3}, y={box.Y:F3}, w={box.W:F3}, h={box.H:F3}, confianza={box.P:F3}");

            Assert.InRange(box.X, 0.1, 0.8);
            Assert.InRange(box.Y, 0.05, 0.8);
            Assert.True(box.P > 0.5);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Reframe_keeps_face_inside_at_least_95_percent_of_frames()
    {
        if (!RunLongTests)
        {
            output.WriteLine("Omitida: faltan los modelos de visión o no está activado RODAJE_PRUEBAS_LARGAS=1.");
            return;
        }

        string? env = TestMediaHelper.FindEnvironment();
        Assert.NotNull(env);
        string portraitPath = Path.Combine(env, "corpus", "vision", "retrato.png");
        if (!File.Exists(portraitPath))
        {
            output.WriteLine($"Omitida: no se encontró {portraitPath}");
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "RastrearTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string inputVideo = Path.Combine(tempDir, "fuente_16_9.mp4");
        string outputVideo = Path.Combine(tempDir, "reencuadrado_9_16.mp4");

        try
        {
            const int width = 960;
            const int height = 540;
            const int frames = 20;
            const double fps = 10.0;
            string ffmpeg = TestMediaHelper.FindFfmpeg();

            // Decodificar retrato
            int pW = 512, pH = 512;
            byte[] portraitBgra = new byte[pW * pH * 4];
            using (var dec = FfmpegTool.StartDecoder(portraitPath, ffmpeg))
            {
                await FfmpegTool.ReadExactBytesAsync(dec.StandardOutput.BaseStream, portraitBgra, 0, portraitBgra.Length, CancellationToken.None);
            }

            // Generar clip 16:9 donde el retrato se desplaza horizontalmente suavemente
            using (Process encoder = FfmpegTool.StartEncoder(inputVideo, width, height, fps, OutputVideoFormat.H264Mp4, ffmpeg))
            {
                Stream stream = encoder.StandardInput.BaseStream;
                byte[] frame = new byte[width * height * 4];
                int targetH = 300;
                int targetW = targetH; // 300x300

                for (int f = 0; f < frames; f++)
                {
                    Array.Clear(frame);
                    // Fondo gris
                    for (int i = 0; i < width * height; i++)
                    {
                        int o = i * 4;
                        frame[o] = 200;
                        frame[o + 1] = 200;
                        frame[o + 2] = 200;
                        frame[o + 3] = 255;
                    }

                    // Posición X del retrato: de x=100 a x=500
                    int left = 100 + (int)(400.0 * f / frames);
                    int top = (height - targetH) / 2;

                    for (int y = 0; y < targetH; y++)
                    {
                        int sy = y * pH / targetH;
                        for (int x = 0; x < targetW; x++)
                        {
                            int sx = x * pW / targetW;
                            int sIdx = ((sy * pW) + sx) * 4;
                            int dIdx = (((top + y) * width) + (left + x)) * 4;
                            double a = portraitBgra[sIdx + 3] / 255.0;

                            frame[dIdx] = (byte)((portraitBgra[sIdx] * a) + (frame[dIdx] * (1.0 - a)));
                            frame[dIdx + 1] = (byte)((portraitBgra[sIdx + 1] * a) + (frame[dIdx + 1] * (1.0 - a)));
                            frame[dIdx + 2] = (byte)((portraitBgra[sIdx + 2] * a) + (frame[dIdx + 2] * (1.0 - a)));
                            frame[dIdx + 3] = 255;
                        }
                    }

                    await stream.WriteAsync(frame);
                }

                await stream.FlushAsync();
                encoder.StandardInput.Close();
                await encoder.WaitForExitAsync();
            }

            string visionExe = TestMediaHelper.FindVisionModule();
            ReframeOutput reframeResult = await VisionService.ReframeAsync(
                inputVideo,
                outputVideo,
                targetAspect: 9.0 / 16.0,
                smoothing: 0.5,
                device: "cpu",
                moduleExePath: visionExe);

            output.WriteLine($"Reencuadre completado. Métrica «dentro»: {reframeResult.Dentro:P1}. Keyframes generados: {reframeResult.Keyframes.Count}");
            Assert.True(File.Exists(outputVideo), "El archivo reencuadrado debe existir.");
            Assert.True(reframeResult.Dentro >= 0.95, $"La cara debe quedar dentro del encuadre en ≥ 95 % de los cuadros (obtenido: {reframeResult.Dentro:P1}).");

            // Medida independiente: rostros detectados en el video recortado, no la cifra que devuelve el módulo.
            DetectionResult comprobacion = await VisionService.DetectAsync(outputVideo, target: "rostros", device: "cpu", moduleExePath: visionExe);
            double medido = comprobacion.Cuadros.Count(c => c.Cajas.Count > 0) / (double)Math.Max(1, comprobacion.Cuadros.Count);
            output.WriteLine($"Rostro detectado en el video recortado: {medido:P1} de {comprobacion.Cuadros.Count} cuadros");
            Assert.True(medido >= 0.95, $"El rostro debe detectarse en ≥ 95 % de los cuadros recortados (obtenido: {medido:P1}).");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
