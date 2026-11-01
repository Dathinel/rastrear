using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

/// <summary>Un cuadro BGRA listo para mostrar: píxeles y tamaño.</summary>
public sealed record PreviewFrame(byte[] Bgra, int Width, int Height);

/// <summary>
/// «Después» del primer cuadro para la vista previa: el recorte del reencuadre (sin seguir al sujeto, centrado como la
/// guía del visor, <see cref="OverlayGeometry.ComputeCropRect"/>) y el fondo quitado con la máscara del módulo sobre un
/// tablero de ajedrez, o la máscara en grises. Seguir, detectar y estabilizar no tienen vista previa: su resultado
/// depende del video entero.
/// </summary>
public static class FramePreview
{
    public static bool SupportsPreview(int toolIndex) => toolIndex is 1 or 3;

    /// <summary>Lo que explica el botón cuando la herramienta no tiene vista previa.</summary>
    public const string NotSupportedText = "La vista previa solo está para «Quitar fondo» y «Reencuadrar»; las demás dependen del video entero.";

    /// <summary>Recorta el cuadro al rectángulo (en píxeles del video), redondeado y encajado dentro de la imagen.</summary>
    public static PreviewFrame Crop(byte[] bgra, int width, int height, UiRect rect)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length < (long)width * height * 4)
        {
            throw new ArgumentException("El cuadro no tiene los bytes de su tamaño.", nameof(bgra));
        }

        int cw = Math.Clamp((int)Math.Round(rect.Width, MidpointRounding.AwayFromZero), 1, width);
        int ch = Math.Clamp((int)Math.Round(rect.Height, MidpointRounding.AwayFromZero), 1, height);
        int left = Math.Clamp((int)Math.Round(rect.X), 0, width - cw);
        int top = Math.Clamp((int)Math.Round(rect.Y), 0, height - ch);

        byte[] output = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
        {
            Buffer.BlockCopy(bgra, (((top + y) * width) + left) * 4, output, y * cw * 4, cw * 4);
        }
        return new PreviewFrame(output, cw, ch);
    }

    /// <summary>El recorte del reencuadre para un preajuste («9:16», «1:1», «4:5»), centrado.</summary>
    public static PreviewFrame CropForAspect(byte[] bgra, int width, int height, string aspectPreset)
    {
        double aspect = OverlayGeometry.AspectRatioFromPreset(aspectPreset);
        return Crop(bgra, width, height, OverlayGeometry.ComputeCropRect(width, height, aspect));
    }

    /// <summary>
    /// Compone la máscara (un byte por píxel, 255 = sujeto) sobre el cuadro: con <paramref name="asMask"/> devuelve la
    /// máscara en grises; si no, el sujeto sobre un tablero de ajedrez (lo que se ve de un video con alfa).
    /// </summary>
    public static PreviewFrame ApplyMask(byte[] bgra, ReadOnlySpan<byte> mask, int width, int height, bool asMask, int checkerSize = 16)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        int pixels = width * height;
        if (bgra.Length < (long)pixels * 4 || mask.Length < pixels)
        {
            throw new ArgumentException("El cuadro o la máscara no tienen los bytes de su tamaño.");
        }

        byte[] output = new byte[pixels * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = (y * width) + x;
                int o = p * 4;
                byte m = mask[p];
                if (asMask)
                {
                    output[o] = m;
                    output[o + 1] = m;
                    output[o + 2] = m;
                }
                else
                {
                    byte back = Checker(x, y, checkerSize);
                    // Mezcla lineal sujeto/tablero por la máscara (255 = todo sujeto)
                    output[o] = (byte)(((bgra[o] * m) + (back * (255 - m)) + 127) / 255);
                    output[o + 1] = (byte)(((bgra[o + 1] * m) + (back * (255 - m)) + 127) / 255);
                    output[o + 2] = (byte)(((bgra[o + 2] * m) + (back * (255 - m)) + 127) / 255);
                }
                output[o + 3] = 255;
            }
        }
        return new PreviewFrame(output, width, height);
    }

    /// <summary>Gris del tablero de ajedrez en (x, y): claro u oscuro por casillas de <paramref name="size"/> píxeles.</summary>
    public static byte Checker(int x, int y, int size) => (((x / size) + (y / size)) & 1) == 0 ? (byte)0xA0 : (byte)0x60;
}
