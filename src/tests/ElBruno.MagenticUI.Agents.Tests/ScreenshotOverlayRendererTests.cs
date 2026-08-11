using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.App;
using SkiaSharp;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ScreenshotOverlayRendererTests
{
    private static byte[] CreateSolidPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void RenderReturnsNullForUndecodableImage()
    {
        var result = ScreenshotOverlayRenderer.Render([1, 2, 3, 4], new FaraAction(FaraActionType.LeftClick), "goal");

        Assert.Null(result);
    }

    [Fact]
    public void RenderProducesValidPngWithSameDimensionsForCoordinateAction()
    {
        var original = CreateSolidPng(400, 300);
        var action = new FaraAction(FaraActionType.RightClick, new FaraCoordinate(500, 500));

        var result = ScreenshotOverlayRenderer.Render(original, action, "click the button");

        Assert.NotNull(result);
        using var decoded = SKBitmap.Decode(result);
        Assert.NotNull(decoded);
        Assert.Equal(400, decoded!.Width);
        Assert.Equal(300, decoded.Height);
    }

    [Fact]
    public void RenderHandlesDragActionWithStartAndEndCoordinates()
    {
        var original = CreateSolidPng(200, 200);
        var action = new FaraAction(
            FaraActionType.LeftClickDrag,
            new FaraCoordinate(100, 100),
            new FaraCoordinate(900, 900));

        var result = ScreenshotOverlayRenderer.Render(original, action, "drag the slider");

        Assert.NotNull(result);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(200, decoded!.Width);
        Assert.Equal(200, decoded.Height);
    }

    [Fact]
    public void RenderHandlesNonCoordinateActionWithBanner()
    {
        var original = CreateSolidPng(320, 240);
        var action = new FaraAction(FaraActionType.Type, Text: "hello world");

        var result = ScreenshotOverlayRenderer.Render(original, action, "type a greeting");

        Assert.NotNull(result);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(320, decoded!.Width);
        Assert.Equal(240, decoded.Height);
    }

    [Theory]
    [InlineData(FaraActionType.LeftClick, "1. Click here")]
    [InlineData(FaraActionType.RightClick, "1. Right-click here")]
    [InlineData(FaraActionType.DoubleClick, "1. Double-click here")]
    [InlineData(FaraActionType.LeftClickDrag, "1. Click and drag to 2")]
    public void BuildInstructionDescribesCoordinateActions(FaraActionType type, string expected)
    {
        var action = new FaraAction(type, new FaraCoordinate(1, 1));

        Assert.Equal(expected, ScreenshotOverlayRenderer.BuildInstruction(action));
    }

    [Fact]
    public void BuildInstructionDescribesTypeAction()
    {
        var action = new FaraAction(FaraActionType.Type, Text: "secret");

        Assert.Equal("Type: \"secret\"", ScreenshotOverlayRenderer.BuildInstruction(action));
    }

    [Fact]
    public void BuildInstructionDescribesKeyAction()
    {
        var action = new FaraAction(FaraActionType.Key, Keys: ["Ctrl", "S"]);

        Assert.Equal("Press keys: Ctrl + S", ScreenshotOverlayRenderer.BuildInstruction(action));
    }

    [Fact]
    public void BuildInstructionDescribesScrollAction()
    {
        var action = new FaraAction(FaraActionType.Scroll, Pixels: 240);

        Assert.Equal("Scroll 240 pixels", ScreenshotOverlayRenderer.BuildInstruction(action));
    }

    [Fact]
    public void BuildInstructionDescribesVisitUrlAction()
    {
        var action = new FaraAction(FaraActionType.VisitUrl, Url: "https://example.com");

        Assert.Equal("Navigate to: https://example.com", ScreenshotOverlayRenderer.BuildInstruction(action));
    }
}
