using Rastrear.Core;

namespace Rastrear.Tests;

public sealed class AtomicFileTests : TemporaryFolderTest
{
    [Fact]
    public void Escribe_el_texto_utf8_exacto_y_sin_bom()
    {
        string ruta = PathOf("archivo.json");
        string contenido = """{"titulo":"niño año — «comillas» "}""";

        AtomicFile.WriteAllText(ruta, contenido);

        byte[] bytes = File.ReadAllBytes(ruta);
        Assert.Equal(contenido, File.ReadAllText(ruta));
        Assert.NotEqual([0xEF, 0xBB, 0xBF], bytes[..3]); // UTF-8 sin BOM
        Assert.False(File.Exists(ruta + AtomicFile.TemporarySuffix));
    }

    [Fact]
    public void Sobre_un_archivo_de_solo_lectura_falla_claro_el_anterior_queda_y_no_deja_temporal()
    {
        Directory.CreateDirectory(Root);
        string ruta = PathOf("preferencias.json");
        File.WriteAllText(ruta, """{"a":1}""");
        File.SetAttributes(ruta, FileAttributes.ReadOnly);
        try
        {
            Exception ex = Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(ruta, """{"a":2}"""));
            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"El error no es claro ({ex.GetType().Name}: {ex.Message})");
            Assert.Equal("""{"a":1}""", File.ReadAllText(ruta)); // el anterior queda intacto
            Assert.Empty(Directory.GetFiles(Root, "*" + AtomicFile.TemporarySuffix)); // y el temporal ya escrito se recoge
        }
        finally
        {
            File.SetAttributes(ruta, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Fallo_al_escribir_el_temporal_no_toca_el_destino()
    {
        Directory.CreateDirectory(Root);
        string ruta = PathOf("preferencias.json");
        File.WriteAllText(ruta, """{"a":1}""");

        // El temporal existe y está abierto con exclusión: escribirlo falla y el destino queda intacto.
        string tmp = ruta + AtomicFile.TemporarySuffix;
        using (FileStream _ = new(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            Assert.Throws<IOException>(() => AtomicFile.WriteAllText(ruta, """{"a":2}"""));
            Assert.Equal("""{"a":1}""", File.ReadAllText(ruta));
        }
    }
}
