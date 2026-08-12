using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.App;
using Xunit;

namespace ElBruno.MagenticUI.Agents.Tests;

public class ClientPredictionOverlayMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 50)]
    [InlineData(1000, 100)]
    [InlineData(250, 25)]
    public void ToPercent_ConvertsFara0To1000SpaceIntoPercent(int coordinateValue, double expectedPercent)
    {
        // Arrange & Act
        var percent = ClientPredictionOverlayMath.ToPercent(coordinateValue);

        // Assert
        Assert.Equal(expectedPercent, percent, precision: 6);
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(1500)]
    public void ToPercent_ClampsOutOfRangeValues(int coordinateValue)
    {
        // Arrange & Act
        var percent = ClientPredictionOverlayMath.ToPercent(coordinateValue);

        // Assert
        Assert.InRange(percent, 0, 100);
    }

    [Fact]
    public void MarkerPointStyle_ProducesLeftTopPercentageCss()
    {
        // Arrange
        var coordinate = new FaraCoordinate(250, 750);

        // Act
        var style = ClientPredictionOverlayMath.MarkerPointStyle(coordinate);

        // Assert
        Assert.Equal("left:25%;top:75%;", style);
    }
}
