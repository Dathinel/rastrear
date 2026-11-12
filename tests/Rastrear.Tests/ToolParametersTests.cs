using Rastrear.Core.Model;
using Xunit;

namespace Rastrear.Tests;

public class ToolParametersTests
{
    [Fact]
    public void QualityFromIndex_Maps_Index_Correctly()
    {
        Assert.Equal("normal", ToolParameters.QualityFromIndex(0));
        Assert.Equal("alta", ToolParameters.QualityFromIndex(1));
        Assert.Equal("normal", ToolParameters.QualityFromIndex(-1));
        Assert.Equal("normal", ToolParameters.QualityFromIndex(2));
    }

    [Fact]
    public void DeviceFromIndex_Maps_Index_Correctly()
    {
        Assert.Equal("auto", ToolParameters.DeviceFromIndex(0));
        Assert.Equal("gpu", ToolParameters.DeviceFromIndex(1));
        Assert.Equal("cpu", ToolParameters.DeviceFromIndex(2));
        Assert.Equal("auto", ToolParameters.DeviceFromIndex(-1));
        Assert.Equal("auto", ToolParameters.DeviceFromIndex(99));
        Assert.Equal("auto", ToolParameters.DeviceFromIndex(int.MinValue));
        Assert.Equal("auto", ToolParameters.DeviceFromIndex(int.MaxValue));
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("   ", "auto")]
    [InlineData("auto", "auto")]
    [InlineData("gpu", "gpu")]
    [InlineData("cpu", "cpu")]
    [InlineData("GPU", "auto")]   // mayúsculas no normalizan: cae al valor por omisión
    [InlineData(" gpu", "auto")]  // con espacios tampoco
    [InlineData("cpu ", "auto")]
    [InlineData("tpu", "auto")]
    [InlineData("0", "auto")]
    public void NormalizeDevice_falls_back_to_auto(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.NormalizeDevice(input));

    [Theory]
    [InlineData(null, "normal")]
    [InlineData("", "normal")]
    [InlineData("normal", "normal")]
    [InlineData("alta", "alta")]
    [InlineData("ALTA", "normal")]
    [InlineData("alta ", "normal")]
    [InlineData("baja", "normal")]
    public void NormalizeQuality_falls_back_to_normal(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.NormalizeQuality(input));

    [Theory]
    [InlineData(null, "rostros")]
    [InlineData("", "rostros")]
    [InlineData("rostros", "rostros")]
    [InlineData("personas", "personas")]
    [InlineData("PERSONAS", "rostros")]
    [InlineData("rostros ", "rostros")]
    [InlineData("coches", "rostros")]
    public void NormalizeDetectTarget_falls_back_to_rostros(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.NormalizeDetectTarget(input));

    [Theory]
    [InlineData("rostros", true)]
    [InlineData("personas", true)]
    [InlineData("Rostros", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("personas ", false)]
    public void IsDetectTarget_is_exact_and_case_sensitive(string? input, bool expected) =>
        Assert.Equal(expected, ToolParameters.IsDetectTarget(input));

    [Theory]
    [InlineData("9:16", "9:16")]
    [InlineData("1:1", "1:1")]
    [InlineData("4:5", "4:5")]
    [InlineData("16:9", "9:16")]
    [InlineData("3:2", "9:16")]
    [InlineData(null, "9:16")]
    [InlineData("", "9:16")]
    public void NormalizeAspectPreset_falls_back_to_916(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.NormalizeAspectPreset(input));

    [Theory]
    [InlineData("punto", "punto")]
    [InlineData("region", "region")]
    [InlineData("región", "punto")] // con tilde no vale: cae a «punto»
    [InlineData("PUNTO", "punto")]
    [InlineData(null, "punto")]
    [InlineData("", "punto")]
    public void NormalizeFollowMode_falls_back_to_punto(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.NormalizeFollowMode(input));

    [Theory]
    [InlineData("cpu", "cpu")]
    [InlineData("gpu", "gpu")]
    [InlineData("auto", "gpu")] // «auto» es gpu, que ya cae a CPU si no hay DirectML
    [InlineData(null, "gpu")]
    [InlineData("", "gpu")]
    [InlineData("tensor", "gpu")]
    public void ComputeDevice_is_cpu_or_gpu_only(string? input, string expected) =>
        Assert.Equal(expected, ToolParameters.ComputeDevice(input));

    [Theory]
    [InlineData(0.5, 0.1, 0.9, 0.5, 0.5)]          // dentro del rango: tal cual
    [InlineData(-5.0, 0.1, 0.9, 0.5, 0.1)]         // por debajo: el mínimo
    [InlineData(5.0, 0.1, 0.9, 0.5, 0.9)]          // por encima: el máximo
    [InlineData(0.1, 0.1, 0.9, 0.5, 0.1)]          // en el borde exacto
    [InlineData(0.9, 0.1, 0.9, 0.5, 0.9)]
    [InlineData(double.NaN, 0.1, 0.9, 0.5, 0.5)]   // NaN → el valor por omisión
    [InlineData(double.PositiveInfinity, 0.1, 0.9, 0.5, 0.5)]
    [InlineData(double.NegativeInfinity, 0.1, 0.9, 0.5, 0.5)]
    public void ClampFinite_clamps_or_falls_back(double value, double min, double max, double fallback, double expected) =>
        Assert.Equal(expected, ToolParameters.ClampFinite(value, min, max, fallback));
}
