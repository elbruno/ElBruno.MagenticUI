using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class FaraActionParserTests
{
    private readonly FaraActionParser _parser = new();

    [Fact]
    public void ParsesToolCallClick()
    {
        var result = _parser.Parse("""<tool_call>{"name":"computer_use","arguments":{"action":"left_click","coordinate":[100,200]}}</tool_call>""");

        Assert.True(result.Success);
        Assert.Equal(FaraActionType.LeftClick, result.Action!.Type);
        Assert.Equal(new FaraCoordinate(100, 200), result.Action.Coordinate);
    }

    [Fact]
    public void ParsesTextAndKeys()
    {
        var type = _parser.Parse("""{"action":"type","text":"hello"}""");
        var key = _parser.Parse("""{"action":"key","keys":["CTRL","A"]}""");

        Assert.Equal("hello", type.Action!.Text);
        Assert.Equal(["CTRL", "A"], key.Action!.Keys);
    }

    [Fact]
    public void ParsesFencedJsonAndScroll()
    {
        var click = _parser.Parse("""
            ```json
            {"action":"double_click","coordinate":[12,34]}
            ```
            """);
        var scroll = _parser.Parse("""{"action":"scroll","pixels":500}""");

        Assert.True(click.Success);
        Assert.Equal(new FaraCoordinate(12, 34), click.Action!.Coordinate);
        Assert.True(scroll.Success);
        Assert.Equal(500, scroll.Action!.Pixels);
    }

    [Fact]
    public void RejectsUnsupportedAction()
    {
        var result = _parser.Parse("""{"action":"terminate","answer":"done"}""");

        Assert.False(result.Success);
        Assert.Contains("Unsupported", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"action":"left_click","coordinate":[1]}""")]
    [InlineData("""{"action":"type","text":""}""")]
    public void RejectsMalformedActions(string response)
    {
        var result = _parser.Parse(response);

        Assert.False(result.Success);
        Assert.Null(result.Action);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ParsesOfficialDragShapeWithDestinationCoordinate()
    {
        var result = _parser.Parse("""{"action":"left_click_drag","coordinate":[320,240]}""");

        Assert.True(result.Success);
        Assert.Equal(new FaraCoordinate(320, 240), result.Action!.Coordinate);
        Assert.Null(result.Action.EndCoordinate);
    }

    [Fact]
    public void ScalesAndClampsCoordinates()
    {
        var result = FaraCoordinateScaler.Scale(new FaraCoordinate(500, 500), 1000, 1000, 400, 300);
        var clamped = FaraCoordinateScaler.Scale(new FaraCoordinate(1200, -10), 1000, 1000, 400, 300);

        Assert.Equal(new FaraCoordinate(200, 150), result);
        Assert.Equal(new FaraCoordinate(400, 0), clamped);
    }

    [Fact]
    public void RejectsNonPositiveImageDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FaraCoordinateScaler.Scale(new FaraCoordinate(1, 1), 0, 100, 400, 300));
    }

    [Fact]
    public void ParsesFirstObjectWhenModelRepeatsItsAnswer()
    {
        // Arrange — Fara repeats the same object until it exhausts its token budget.
        var raw = string.Join('\n', Enumerable.Repeat("""{"action":"left_click","coordinate":[120,240]}""", 5));

        // Act
        var result = _parser.Parse(raw);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(FaraActionType.LeftClick, result.Action!.Type);
        Assert.Equal(new FaraCoordinate(120, 240), result.Action.Coordinate);
    }

    [Fact]
    public void ParsesBoundingBoxGroundingResponseAsClickAtCenter()
    {
        // Arrange / Act
        var result = _parser.Parse("""{"bbox_2d": [228, 488, 268, 518], "label": "New issue"}""");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(FaraActionType.LeftClick, result.Action!.Type);
        Assert.Equal(new FaraCoordinate(248, 503), result.Action.Coordinate);
    }

    [Fact]
    public void ParsesShorthandActionKeyResponse()
    {
        // Arrange / Act — Fara sometimes uses the action name as the property name,
        // and may append a stray closing brace.
        var result = _parser.Parse("""{"right_click": [218, 584]}}""");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(FaraActionType.RightClick, result.Action!.Type);
        Assert.Equal(new FaraCoordinate(218, 584), result.Action.Coordinate);
    }

    [Fact]
    public void ParsesPointGroundingResponse()
    {
        // Arrange / Act
        var result = _parser.Parse("""{"point_2d": [640, 480]}""");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(FaraActionType.LeftClick, result.Action!.Type);
        Assert.Equal(new FaraCoordinate(640, 480), result.Action.Coordinate);
    }
}

