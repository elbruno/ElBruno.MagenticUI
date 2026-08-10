namespace ElBruno.MagenticUI.Agents.Models;

public static class FaraCoordinateScaler
{
    public static FaraCoordinate Scale(
        FaraCoordinate coordinate,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Image dimensions must be positive.");

        var x = coordinate.X * (double)targetWidth / sourceWidth;
        var y = coordinate.Y * (double)targetHeight / sourceHeight;
        return new FaraCoordinate(
            Math.Clamp((int)Math.Round(x, MidpointRounding.AwayFromZero), 0, targetWidth),
            Math.Clamp((int)Math.Round(y, MidpointRounding.AwayFromZero), 0, targetHeight));
    }
}
