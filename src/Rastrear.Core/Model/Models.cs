using System.Text.Json.Serialization;

namespace Rastrear.Core.Model;

public readonly record struct VideoProgress(double Fraction, string Stage);

// Seguir
public sealed record FollowFrame(
    [property: JsonPropertyName("cuadro")] int Cuadro,
    [property: JsonPropertyName("t")] double Tiempo,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("confianza")] double Confianza,
    [property: JsonPropertyName("estado")] string Estado);

public sealed record FollowResult(
    [property: JsonPropertyName("ancho")] int Ancho,
    [property: JsonPropertyName("alto")] int Alto,
    [property: JsonPropertyName("fps")] double Fps,
    [property: JsonPropertyName("cuadros")] IReadOnlyList<FollowFrame> Cuadros);

// Detección
public readonly record struct DetectionBox(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double W,
    [property: JsonPropertyName("h")] double H,
    [property: JsonPropertyName("p")] double P);

public sealed record DetectionFrame(
    [property: JsonPropertyName("cajas")] IReadOnlyList<DetectionBox> Cajas);

public sealed record DetectionResult(
    [property: JsonPropertyName("cuadros")] IReadOnlyList<DetectionFrame> Cuadros,
    [property: JsonPropertyName("dispositivo")] string? Dispositivo = null,
    [property: JsonPropertyName("segundos")] double? Segundos = null);

// Reencuadre
public sealed record ReframeSource(
    [property: JsonPropertyName("ancho")] int Ancho,
    [property: JsonPropertyName("alto")] int Alto);

public sealed record ReframeInputFrame(
    [property: JsonPropertyName("t")] double T,
    [property: JsonPropertyName("cajas")] IReadOnlyList<DetectionBox> Cajas);

public sealed record ReframeParameters(
    [property: JsonPropertyName("aspecto")] double Aspecto,
    [property: JsonPropertyName("fuente")] ReframeSource Fuente,
    [property: JsonPropertyName("cuadros")] IReadOnlyList<ReframeInputFrame> Cuadros,
    [property: JsonPropertyName("suavizado")] double Suavizado);

public readonly record struct ReframeKeyframe(
    [property: JsonPropertyName("t")] double T,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("scale")] double Scale);

public sealed record ReframeOutput(
    [property: JsonPropertyName("keyframes")] IReadOnlyList<ReframeKeyframe> Keyframes,
    [property: JsonPropertyName("dentro")] double Dentro);

// Parámetros Vision
public sealed record MattingParameters(
    [property: JsonPropertyName("calidad")] string? Calidad,
    [property: JsonPropertyName("sesion")] string? Sesion,
    [property: JsonPropertyName("reiniciar")] bool Reiniciar,
    [property: JsonPropertyName("dispositivo")] string? Dispositivo,
    [property: JsonPropertyName("lado")] int? Lado = null,
    [property: JsonPropertyName("hilos")] int? Hilos = null);

public sealed record VisionDetectParameters(
    [property: JsonPropertyName("objetivo")] string Objetivo,
    [property: JsonPropertyName("dispositivo")] string? Dispositivo = null,
    [property: JsonPropertyName("hilos")] int? Hilos = null);

// FFprobe
public sealed record FfprobeStream(
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("r_frame_rate")] string? RFrameRate,
    [property: JsonPropertyName("avg_frame_rate")] string? AvgFrameRate,
    [property: JsonPropertyName("nb_frames")] string? NbFrames,
    [property: JsonPropertyName("duration")] string? Duration);

public sealed record FfprobeFormat(
    [property: JsonPropertyName("duration")] string? Duration);

public sealed record FfprobeOutput(
    [property: JsonPropertyName("streams")] IReadOnlyList<FfprobeStream>? Streams,
    [property: JsonPropertyName("format")] FfprobeFormat? Format);

// Eco
public sealed record EchoCountParameters(
    [property: JsonPropertyName("pasos")] int Pasos,
    [property: JsonPropertyName("msPorPaso")] int MsPorPaso);

public sealed record EchoDieParameters(
    [property: JsonPropertyName("codigo")] int Codigo);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FollowFrame))]
[JsonSerializable(typeof(FollowResult))]
[JsonSerializable(typeof(DetectionBox))]
[JsonSerializable(typeof(DetectionFrame))]
[JsonSerializable(typeof(DetectionResult))]
[JsonSerializable(typeof(ReframeSource))]
[JsonSerializable(typeof(ReframeInputFrame))]
[JsonSerializable(typeof(ReframeParameters))]
[JsonSerializable(typeof(ReframeKeyframe))]
[JsonSerializable(typeof(ReframeOutput))]
[JsonSerializable(typeof(MattingParameters))]
[JsonSerializable(typeof(VisionDetectParameters))]
[JsonSerializable(typeof(FfprobeStream))]
[JsonSerializable(typeof(FfprobeFormat))]
[JsonSerializable(typeof(FfprobeOutput))]
[JsonSerializable(typeof(EchoCountParameters))]
[JsonSerializable(typeof(EchoDieParameters))]
[JsonSerializable(typeof(Rastrear.Core.Ui.WindowPlacement))]
internal partial class RastrearJsonContext : JsonSerializerContext
{
}
