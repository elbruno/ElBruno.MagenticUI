using ElBruno.MagenticUI.Agents.Models;
using SkiaSharp;

namespace ElBruno.MagenticUI.App;

/// <summary>
/// Draws Fara's predicted action on top of the original screenshot (marker, arrow,
/// and instruction label) so the result can be shown to the user as an annotated
/// "what to do" image. Rendering-only: it never opens a browser or performs the action.
/// </summary>
public static class ScreenshotOverlayRenderer
{
    private const float MarkerRadius = 22f;
    private const float LabelPadding = 12f;
    private const float LabelFontSize = 22f;

    /// <summary>
    /// Renders an annotated PNG copy of <paramref name="originalImageBytes"/> highlighting
    /// where Fara predicted the next action should happen and what that action is.
    /// Returns null if the image cannot be decoded.
    /// </summary>
    public static byte[]? Render(byte[] originalImageBytes, FaraAction action, string goal)
    {
        SKBitmap? original;
        try
        {
            original = SKBitmap.Decode(originalImageBytes);
        }
        catch (Exception)
        {
            // SKBitmap.Decode throws (rather than returning null) for some malformed
            // inputs; normalize both failure modes to the documented null contract.
            return null;
        }

        using (original)
        {
            if (original is null)
                return null;

            return RenderCore(original, action);
        }
    }

    private static byte[] RenderCore(SKBitmap original, FaraAction action)
    {
        using var surface = SKSurface.Create(new SKImageInfo(original.Width, original.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(original, 0, 0);

        var instruction = BuildInstruction(action);

        if (action.Coordinate is not null)
        {
            var point = ToPixel(action.Coordinate, original.Width, original.Height);
            SKPoint? endPoint = action.Type == FaraActionType.LeftClickDrag && action.EndCoordinate is not null
                ? ToPixel(action.EndCoordinate, original.Width, original.Height)
                : null;

            if (endPoint is { } end)
            {
                DrawMarker(canvas, point, "1");
                DrawMarker(canvas, end, "2");
                DrawArrow(canvas, point, end);
                DrawLabel(canvas, instruction, point, original.Width, original.Height);
            }
            else
            {
                DrawMarker(canvas, point, "1");
                DrawLabel(canvas, instruction, point, original.Width, original.Height);
            }
        }
        else
        {
            DrawBanner(canvas, instruction, original.Width);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKPoint ToPixel(FaraCoordinate coordinate, int width, int height) =>
        new(
            (float)Math.Clamp(coordinate.X / 1000d, 0, 1) * width,
            (float)Math.Clamp(coordinate.Y / 1000d, 0, 1) * height);

    private static void DrawMarker(SKCanvas canvas, SKPoint point, string step)
    {
        using var haloFill = new SKPaint
        {
            Color = new SKColor(220, 38, 38, 90),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var ringStroke = new SKPaint
        {
            Color = new SKColor(220, 38, 38, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true
        };
        using var crosshair = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true
        };
        using var stepFill = new SKPaint
        {
            Color = new SKColor(220, 38, 38, 255),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var stepText = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 16,
            TextAlign = SKTextAlign.Center
        };

        canvas.DrawCircle(point, MarkerRadius, haloFill);
        canvas.DrawCircle(point, MarkerRadius, ringStroke);
        canvas.DrawLine(point.X - 10, point.Y, point.X + 10, point.Y, crosshair);
        canvas.DrawLine(point.X, point.Y - 10, point.X, point.Y + 10, crosshair);

        var badgeCenter = new SKPoint(point.X + MarkerRadius, point.Y - MarkerRadius);
        canvas.DrawCircle(badgeCenter, 12, stepFill);
        canvas.DrawText(step, badgeCenter.X, badgeCenter.Y + 5, stepText);
    }

    private static void DrawArrow(SKCanvas canvas, SKPoint from, SKPoint to)
    {
        using var linePaint = new SKPaint
        {
            Color = new SKColor(37, 99, 235, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([14, 10], 0)
        };
        canvas.DrawLine(from, to, linePaint);
    }

    private static void DrawLabel(SKCanvas canvas, string text, SKPoint anchor, int width, int height)
    {
        using var textPaint = new SKPaint { TextSize = LabelFontSize, IsAntialias = true };
        var textWidth = textPaint.MeasureText(text);
        var boxWidth = textWidth + LabelPadding * 2;
        var boxHeight = LabelFontSize + LabelPadding * 2;

        // Prefer placing the label below-right of the marker; flip to stay on-canvas.
        var boxX = Math.Clamp(anchor.X + MarkerRadius + 10, 4, Math.Max(4, width - boxWidth - 4));
        var boxY = anchor.Y + MarkerRadius + 40 + boxHeight <= height
            ? anchor.Y + MarkerRadius + 16
            : Math.Max(4, anchor.Y - MarkerRadius - 16 - boxHeight);

        DrawLabelBox(canvas, text, boxX, boxY, boxWidth, boxHeight);
    }

    private static void DrawBanner(SKCanvas canvas, string text, int width)
    {
        using var textPaint = new SKPaint { TextSize = LabelFontSize, IsAntialias = true };
        var textWidth = Math.Min(textPaint.MeasureText(text), width - LabelPadding * 2 - 8);
        var boxWidth = Math.Min(textWidth + LabelPadding * 2, width - 8);
        var boxHeight = LabelFontSize + LabelPadding * 2;
        var boxX = Math.Max(4, (width - boxWidth) / 2);
        const float boxY = 16;

        DrawLabelBox(canvas, text, boxX, boxY, boxWidth, boxHeight);
    }

    private static void DrawLabelBox(SKCanvas canvas, string text, float x, float y, float w, float h)
    {
        using var backgroundFill = new SKPaint { Color = new SKColor(17, 24, 39, 235), IsAntialias = true };
        using var border = new SKPaint
        {
            Color = new SKColor(220, 38, 38, 255),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = LabelFontSize
        };

        var rect = new SKRoundRect(new SKRect(x, y, x + w, y + h), 8, 8);
        canvas.DrawRoundRect(rect, backgroundFill);
        canvas.DrawRoundRect(rect, border);
        canvas.DrawText(text, x + LabelPadding, y + h - LabelPadding - 2, textPaint);
    }

    /// <summary>
    /// Converts a parsed Fara action into a short imperative instruction describing
    /// where/what to click, type, or navigate to.
    /// </summary>
    public static string BuildInstruction(FaraAction action) =>
        action.Type switch
        {
            FaraActionType.LeftClick => "1. Click here",
            FaraActionType.RightClick => "1. Right-click here",
            FaraActionType.DoubleClick => "1. Double-click here",
            FaraActionType.LeftClickDrag => "1. Click and drag to 2",
            FaraActionType.Type => $"Type: \"{action.Text}\"",
            FaraActionType.Key => $"Press keys: {string.Join(" + ", action.Keys ?? [])}",
            FaraActionType.Scroll => $"Scroll {action.Pixels:0.##} pixels",
            FaraActionType.VisitUrl => $"Navigate to: {action.Url}",
            _ => "Perform the predicted action"
        };
}
