using Rastrear.Core.Model;
using Rastrear.Core.Ui;

namespace Rastrear.Tests;

public class VistaPreviaTests
{
    private static byte[] Cuadro(int w, int h, Func<int, int, (byte B, byte G, byte R)> color)
    {
        byte[] bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte b, byte g, byte r) = color(x, y);
                int o = ((y * w) + x) * 4;
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
            }
        }
        return bgra;
    }

    [Fact]
    public void Recorte_del_reencuadre_copia_los_pixeles_del_rectangulo_centrado()
    {
        // 16×8: la mitad izquierda es roja, la derecha azul; el recorte 1:1 (8×8) centrado toma 4 columnas de cada una.
        byte[] bgra = Cuadro(16, 8, (x, _) => x < 8 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)0, (byte)0));
        PreviewFrame p = FramePreview.CropForAspect(bgra, 16, 8, "1:1");
        Assert.Equal(8, p.Width);
        Assert.Equal(8, p.Height);
        Assert.Equal(8 * 8 * 4, p.Bgra.Length);
        Assert.Equal(255, p.Bgra[2]); // (0,0): rojo (columna 4 del original)
        Assert.Equal(255, p.Bgra[(7 * 4)]); // (7,0): azul (columna 11)
        Assert.Equal(255, p.Bgra[(((7 * 8) + 3) * 4) + 2]); // (3,7): rojo

        // 9:16 sobre 16×8: alto 8, ancho 4,5 → 5 (redondeado) y dentro de la imagen
        PreviewFrame v = FramePreview.CropForAspect(bgra, 16, 8, "9:16");
        Assert.Equal(5, v.Width);
        Assert.Equal(8, v.Height);

        // Un rectángulo que se sale se encaja
        PreviewFrame r = FramePreview.Crop(bgra, 16, 8, new UiRect(14, -3, 10, 20));
        Assert.Equal(10, r.Width);
        Assert.Equal(8, r.Height);
        Assert.Equal(255, r.Bgra[2]); // Empieza en la columna 6: roja
        Assert.Equal(255, r.Bgra[(9 * 4)]); // Acaba en la 15: azul
    }

    [Fact]
    public void Mascara_sobre_tablero_o_en_grises()
    {
        byte[] bgra = Cuadro(4, 2, (_, _) => ((byte)10, (byte)20, (byte)30));
        byte[] mask = [255, 0, 128, 255, 0, 0, 255, 255];

        PreviewFrame alfa = FramePreview.ApplyMask(bgra, mask, 4, 2, asMask: false, checkerSize: 1);
        Assert.Equal([10, 20, 30, 255], alfa.Bgra[0..4]); // Sujeto entero: el color original
        byte tablero = FramePreview.Checker(1, 0, 1);
        Assert.Equal([tablero, tablero, tablero, 255], alfa.Bgra[4..8]); // Fondo entero: el tablero
        byte mitad = (byte)(((30 * 128) + (FramePreview.Checker(2, 0, 1) * 127) + 127) / 255);
        Assert.Equal(mitad, alfa.Bgra[(2 * 4) + 2]); // Mezcla al 50 %

        PreviewFrame gris = FramePreview.ApplyMask(bgra, mask, 4, 2, asMask: true);
        Assert.Equal([255, 255, 255, 255], gris.Bgra[0..4]);
        Assert.Equal([0, 0, 0, 255], gris.Bgra[4..8]);
        Assert.Equal([128, 128, 128, 255], gris.Bgra[8..12]);

        Assert.NotEqual(FramePreview.Checker(0, 0, 16), FramePreview.Checker(16, 0, 16));
        Assert.Equal(FramePreview.Checker(0, 0, 16), FramePreview.Checker(16, 16, 16));

        Assert.True(FramePreview.SupportsPreview(1));
        Assert.True(FramePreview.SupportsPreview(3));
        Assert.False(FramePreview.SupportsPreview(0));
        Assert.Throws<ArgumentException>(() => FramePreview.ApplyMask(bgra, mask, 4, 3, asMask: true));
    }
}
