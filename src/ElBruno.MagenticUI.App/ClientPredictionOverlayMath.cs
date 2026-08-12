using ElBruno.MagenticUI.Agents.Models;

namespace ElBruno.MagenticUI.App;

/// <summary>
/// Pure coordinate-conversion helpers for the client-side predicted-action overlay
/// drawn directly in the browser (see FaraVisualGrounding.razor). Kept separate from
/// the Razor component so the math can be unit tested without a component test host.
/// </summary>
public static class ClientPredictionOverlayMath
{
    /// <summary>
    /// Converts a coordinate from Fara's 0-1000 action space into a 0-100 percentage,
    /// matching the same scale <see cref="ScreenshotOverlayRenderer"/> uses for the
    /// server-rendered overlay so both presentations agree on marker placement.
    /// </summary>
    public static double ToPercent(int coordinateValue) => Math.Clamp(coordinateValue / 10d, 0, 100);

    /// <summary>
    /// Builds the inline CSS <c>left</c>/<c>top</c> percentage style for an absolutely
    /// positioned marker over a responsive preview image.
    /// </summary>
    public static string MarkerPointStyle(FaraCoordinate coordinate) =>
        $"left:{ToPercent(coordinate.X):0.###}%;top:{ToPercent(coordinate.Y):0.###}%;";
}
