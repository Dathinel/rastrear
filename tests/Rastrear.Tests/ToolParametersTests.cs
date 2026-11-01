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
    }
}
