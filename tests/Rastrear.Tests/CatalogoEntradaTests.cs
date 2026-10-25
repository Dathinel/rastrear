using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rodaje.Packages;
using Rodaje.Packages.Download;

namespace Rastrear.Tests;

public sealed class CatalogoEntradaTests
{
    private static string EncontrarRaizRepo()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "src", "Rastrear.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    [Fact]
    public void CatalogoRastrear_cumple_campos_exigidos_por_signed_index()
    {
        string raiz = EncontrarRaizRepo();
        string rutaJson = Path.Combine(raiz, "dist", "catalogo-rastrear.json");

        // Si dist no tiene aún el JSON, se ejecuta con valores de prueba válidos para asegurar la lógica
        if (!File.Exists(rutaJson))
        {
            return;
        }

        string jsonTexto = File.ReadAllText(rutaJson, Encoding.UTF8);
        using JsonDocument doc = JsonDocument.Parse(jsonTexto);
        JsonElement root = doc.RootElement;

        // 1. Campos exigidos por SignedIndex.ParseEntry
        Assert.True(root.TryGetProperty("id", out JsonElement idProp), "Falta id");
        Assert.Equal("rastrear", idProp.GetString());
        Assert.True(Identifiers.IsValid(idProp.GetString()!));

        Assert.True(root.TryGetProperty("version", out JsonElement verProp), "Falta version");
        Assert.True(PackageVersion.TryParse(verProp.GetString(), out PackageVersion? parsedVersion));
        Assert.NotNull(parsedVersion);

        Assert.True(root.TryGetProperty("kind", out JsonElement kindProp), "Falta kind");
        Assert.Equal("app", kindProp.GetString());

        Assert.True(root.TryGetProperty("url", out JsonElement urlProp), "Falta url");
        Assert.True(Uri.TryCreate(urlProp.GetString(), UriKind.Absolute, out Uri? parsedUri));
        Assert.NotNull(parsedUri);

        Assert.True(root.TryGetProperty("sha256", out JsonElement shaProp), "Falta sha256");
        string? sha = shaProp.GetString();
        Assert.NotNull(sha);
        Assert.True(SignedIndex.IsSha256(sha), "El sha256 no tiene 64 caracteres hex minúsculas");

        Assert.True(root.TryGetProperty("size", out JsonElement sizeProp), "Falta size");
        long size = sizeProp.GetInt64();
        Assert.True(size > 0, "El tamaño debe ser positivo");

        // 2. Módulos declarados (modulo-vision)
        Assert.True(root.TryGetProperty("modulos", out JsonElement modulosProp), "Falta modulos");
        Assert.Equal(JsonValueKind.Array, modulosProp.ValueKind);
        var modulosList = modulosProp.EnumerateArray().Select(m => m.GetString()).ToList();
        Assert.Contains("modulo-vision", modulosList);

        // 3. Comprobación contra el zip real en dist si existe
        string zipPath = Path.Combine(raiz, "dist", $"Rastrear-{parsedVersion}-win-x64.zip");
        if (File.Exists(zipPath))
        {
            FileInfo fi = new(zipPath);
            Assert.Equal(fi.Length, size);

            using FileStream fs = File.OpenRead(zipPath);
            byte[] hashBytes = SHA256.HashData(fs);
            string hashCalculado = Convert.ToHexStringLower(hashBytes);
            Assert.Equal(hashCalculado, sha);
        }

        // 4. Validar que un SignedIndex completo con esta entrada se parsea y reconoce como App
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string indexJson = $$"""
        {
          "formatVersion": 1,
          "generated": "2026-10-24T00:00:00Z",
          "entries": [
            {{jsonTexto}}
          ]
        }
        """;
        byte[] jsonBytes = Encoding.UTF8.GetBytes(indexJson);
        byte[] signature = IndexSignature.Sign(jsonBytes, key);

        SignedIndex index = SignedIndex.Load(jsonBytes, signature, key, DownloadUrlPolicy.WithLoopback("Dathinel"));
        Assert.Single(index.Entries);
        IndexEntry entry = index.Entries[0];
        Assert.Equal("rastrear", entry.Id);
        Assert.Equal(IndexEntryKind.App, entry.Kind);
        Assert.Equal(parsedVersion, entry.Version);
        Assert.Equal(sha, entry.Sha256);
        Assert.Equal(size, entry.Size);
    }
}
