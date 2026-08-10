namespace ElBruno.MagenticUI.Agents.Models;

public enum FaraActionType
{
    LeftClick,
    RightClick,
    DoubleClick,
    LeftClickDrag,
    Type,
    Key,
    Scroll,
    VisitUrl
}

public sealed record FaraCoordinate(int X, int Y);

public sealed record FaraAction(
    FaraActionType Type,
    FaraCoordinate? Coordinate = null,
    FaraCoordinate? EndCoordinate = null,
    string? Text = null,
    IReadOnlyList<string>? Keys = null,
    double? Pixels = null,
    string? Url = null);

public sealed record FaraActionParseResult(
    bool Success,
    FaraAction? Action,
    string RawResponse,
    string? Error = null)
{
    public static FaraActionParseResult Succeeded(FaraAction action, string rawResponse) =>
        new(true, action, rawResponse);

    public static FaraActionParseResult Failed(string rawResponse, string error) =>
        new(false, null, rawResponse, error);
}
