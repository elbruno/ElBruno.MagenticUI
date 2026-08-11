using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElBruno.MagenticUI.Agents.Models;

namespace ElBruno.MagenticUI.Agents.Tools;

public sealed class FaraActionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Matches Fara's native tool-call syntax, optionally namespaced (e.g. <c>computer.scroll(-300)</c>).
    /// <c>left_click_drag</c> precedes <c>left_click</c> so the longer name wins.
    /// </summary>
    private static readonly Regex CallSyntaxPattern = new(
        @"(?:^|[^\w])(?<name>left_click_drag|left_click|right_click|double_click|visit_url|type|key|scroll)\s*\(\s*(?<args>[^()]*?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    public FaraActionParseResult Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return FaraActionParseResult.Failed(rawResponse, "The Fara response is empty.");

        var json = ExtractJson(rawResponse);
        if (json is null)
        {
            // Fara is trained to emit its native tool-call syntax (for example
            // "left_click(178, 594)") and often ignores a JSON instruction, usually after a
            // sentence of reasoning. Accept that shape instead of discarding a valid action.
            return TryParseCallSyntax(rawResponse)
                ?? FaraActionParseResult.Failed(rawResponse, "The Fara response did not contain a JSON action.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            // Fara answers pure grounding prompts with {"bbox_2d": [x1,y1,x2,y2], "label": ...}
            // or {"point_2d": [x,y]} instead of an action envelope. Treat those as a predicted
            // left click at the located element so the page can still show a target.
            var grounding = TryParseGrounding(document.RootElement, rawResponse);
            if (grounding is not null)
                return grounding;

            // Shorthand shape: {"right_click": [218, 584]} — the action name is the property
            // name rather than an "action" value.
            var shorthand = TryParseShorthand(document.RootElement, rawResponse);
            if (shorthand is not null)
                return shorthand;

            var actionObject = FindActionObject(document.RootElement);
            if (actionObject is null)
                return FaraActionParseResult.Failed(rawResponse, "The Fara response did not contain an action object.");

            var actionName = GetString(actionObject.Value, "action");
            if (string.IsNullOrWhiteSpace(actionName))
                return FaraActionParseResult.Failed(rawResponse, "The Fara action is missing its 'action' value.");

            return ParseAction(actionName, actionObject.Value, rawResponse);
        }
        catch (JsonException ex)
        {
            return FaraActionParseResult.Failed(rawResponse, $"The Fara response contained invalid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles responses where the action name is the property name and its value carries the
    /// argument directly, e.g. <c>{"right_click": [218, 584]}</c> or <c>{"type": "hello"}</c>.
    /// Returns <see langword="null"/> when the response does not use that shape.
    /// </summary>
    private static FaraActionParseResult? TryParseShorthand(JsonElement root, string rawResponse)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in root.EnumerateObject())
        {
            var type = property.Name.Trim().ToLowerInvariant() switch
            {
                "left_click" => FaraActionType.LeftClick,
                "right_click" => FaraActionType.RightClick,
                "double_click" => FaraActionType.DoubleClick,
                "left_click_drag" => FaraActionType.LeftClickDrag,
                "type" => FaraActionType.Type,
                "key" => FaraActionType.Key,
                "scroll" => FaraActionType.Scroll,
                "visit_url" => FaraActionType.VisitUrl,
                _ => (FaraActionType?)null
            };

            if (type is null)
                continue;

            var value = property.Value;

            switch (type)
            {
                case FaraActionType.Type when value.ValueKind == JsonValueKind.String:
                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.Type, Text: value.GetString()), rawResponse);

                case FaraActionType.VisitUrl when value.ValueKind == JsonValueKind.String:
                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.VisitUrl, Url: value.GetString()), rawResponse);

                case FaraActionType.Scroll when value.ValueKind == JsonValueKind.Number &&
                                                value.TryGetDouble(out var pixels):
                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.Scroll, Pixels: pixels), rawResponse);

                case FaraActionType.Key when value.ValueKind == JsonValueKind.Array:
                    var keys = value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToArray();
                    if (keys.Length > 0)
                        return FaraActionParseResult.Succeeded(
                            new FaraAction(FaraActionType.Key, Keys: keys), rawResponse);
                    break;

                default:
                    if (TryReadCoordinateArray(value, out var start, out var end))
                    {
                        return FaraActionParseResult.Succeeded(
                            new FaraAction(type.Value, start, type is FaraActionType.LeftClickDrag ? end : null),
                            rawResponse);
                    }
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a <c>[x, y]</c> coordinate, or a <c>[x1, y1, x2, y2]</c> pair, from a JSON array.
    /// </summary>
    private static bool TryReadCoordinateArray(
        JsonElement value,
        out FaraCoordinate? start,
        out FaraCoordinate? end)
    {
        start = null;
        end = null;

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2)
            return false;

        if (!value[0].TryGetInt32(out var x) || !value[1].TryGetInt32(out var y))
            return false;

        start = new FaraCoordinate(x, y);

        if (value.GetArrayLength() >= 4 &&
            value[2].TryGetInt32(out var x2) &&
            value[3].TryGetInt32(out var y2))
        {
            end = new FaraCoordinate(x2, y2);
        }

        return true;
    }

    /// <summary>
    /// Converts Fara's visual-grounding response shapes into a left-click prediction.
    /// Returns <see langword="null"/> when the response is not a grounding response.
    /// </summary>
    private static FaraActionParseResult? TryParseGrounding(JsonElement root, string rawResponse)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (TryGetCoordinate(root, "point_2d", out var point, out _) && point is not null)
            return FaraActionParseResult.Succeeded(new FaraAction(FaraActionType.LeftClick, point), rawResponse);

        if (!root.TryGetProperty("bbox_2d", out var box) ||
            box.ValueKind != JsonValueKind.Array ||
            box.GetArrayLength() < 4)
        {
            return null;
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!box[i].TryGetInt32(out values[i]))
                return null;
        }

        var center = new FaraCoordinate((values[0] + values[2]) / 2, (values[1] + values[3]) / 2);
        return FaraActionParseResult.Succeeded(new FaraAction(FaraActionType.LeftClick, center), rawResponse);
    }

    /// <summary>
    /// Parses Fara's native tool-call syntax, e.g. <c>left_click(178, 594)</c>,
    /// <c>type("hello")</c> or <c>computer.scroll(-300)</c>. The last call in the response wins,
    /// because Fara typically reasons in prose first and emits the action last.
    /// Returns <see langword="null"/> when no supported call is present.
    /// </summary>
    private static FaraActionParseResult? TryParseCallSyntax(string rawResponse)
    {
        for (var match = LastSupportedCall(rawResponse); match is not null; match = null)
        {
            var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
            var arguments = SplitArguments(match.Groups["args"].Value);

            switch (name)
            {
                case "left_click":
                case "right_click":
                case "double_click":
                {
                    if (!TryReadCoordinate(arguments, 0, out var coordinate))
                        break;

                    var type = name switch
                    {
                        "right_click" => FaraActionType.RightClick,
                        "double_click" => FaraActionType.DoubleClick,
                        _ => FaraActionType.LeftClick
                    };
                    return FaraActionParseResult.Succeeded(new FaraAction(type, coordinate), rawResponse);
                }

                case "left_click_drag":
                {
                    if (!TryReadCoordinate(arguments, 0, out var start))
                        break;

                    TryReadCoordinate(arguments, 2, out var end);
                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.LeftClickDrag, start, end), rawResponse);
                }

                case "type":
                {
                    var text = Unquote(arguments.FirstOrDefault());
                    if (string.IsNullOrWhiteSpace(text))
                        break;

                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.Type, Text: text), rawResponse);
                }

                case "visit_url":
                {
                    var url = Unquote(arguments.FirstOrDefault());
                    if (string.IsNullOrWhiteSpace(url))
                        break;

                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.VisitUrl, Url: url), rawResponse);
                }

                case "key":
                {
                    var keys = arguments
                        .Select(Unquote)
                        .Where(key => !string.IsNullOrWhiteSpace(key))
                        .Select(key => key!)
                        .ToArray();
                    if (keys.Length == 0)
                        break;

                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.Key, Keys: keys), rawResponse);
                }

                case "scroll":
                {
                    // Fara emits either scroll(pixels) or scroll(x, y, pixels); the scroll
                    // distance is always the last numeric argument.
                    var numbers = arguments
                        .Select(argument => double.TryParse(
                            argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                                ? value
                                : (double?)null)
                        .Where(value => value is not null)
                        .Select(value => value!.Value)
                        .ToArray();
                    if (numbers.Length == 0)
                        break;

                    return FaraActionParseResult.Succeeded(
                        new FaraAction(FaraActionType.Scroll, Pixels: numbers[^1]), rawResponse);
                }
            }
        }

        return null;
    }

    private static Match? LastSupportedCall(string rawResponse)
    {
        var matches = CallSyntaxPattern.Matches(rawResponse);
        return matches.Count == 0 ? null : matches[^1];
    }

    private static bool TryReadCoordinate(IReadOnlyList<string> arguments, int offset, out FaraCoordinate? coordinate)
    {
        coordinate = null;
        if (arguments.Count < offset + 2)
            return false;

        if (!int.TryParse(arguments[offset], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(arguments[offset + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        coordinate = new FaraCoordinate(x, y);
        return true;
    }

    /// <summary>
    /// Splits a call's argument list on commas that are not inside a quoted string.
    /// </summary>
    private static string[] SplitArguments(string arguments)
    {
        var results = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        var escaped = false;

        foreach (var c in arguments)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                current.Append(c);
                escaped = true;
                continue;
            }

            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote)
                    quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    current.Append(c);
                    break;
                case ',':
                    results.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
            results.Add(current.ToString().Trim());

        return results.Where(argument => argument.Length > 0).ToArray();
    }

    private static string? Unquote(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        var text = argument.Trim();

        // Named arguments such as text="hello" are also emitted occasionally.
        var equals = text.IndexOf('=');
        if (equals > 0 && text[..equals].Trim().All(c => char.IsLetterOrDigit(c) || c == '_'))
            text = text[(equals + 1)..].Trim();

        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            text = text[1..^1];

        return text.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\n", "\n");
    }

    private static FaraActionParseResult ParseAction(
        string actionName,
        JsonElement value,
        string rawResponse)
    {
        switch (actionName.Trim().ToLowerInvariant())
        {
            case "left_click":
                return CoordinateAction(FaraActionType.LeftClick, value, rawResponse);
            case "right_click":
                return CoordinateAction(FaraActionType.RightClick, value, rawResponse);
            case "double_click":
                return CoordinateAction(FaraActionType.DoubleClick, value, rawResponse);
            case "left_click_drag":
                return DragAction(value, rawResponse);
            case "type":
                return StringAction(FaraActionType.Type, "text", value, rawResponse);
            case "key":
                return KeyAction(value, rawResponse);
            case "scroll":
                return NumberAction(FaraActionType.Scroll, value, rawResponse);
            case "visit_url":
                return StringAction(FaraActionType.VisitUrl, "url", value, rawResponse);
            default:
                return FaraActionParseResult.Failed(rawResponse, $"Unsupported Fara action '{actionName}'.");
        }
    }

    private static FaraActionParseResult CoordinateAction(
        FaraActionType type,
        JsonElement value,
        string rawResponse)
    {
        if (!TryGetCoordinate(value, "coordinate", out var coordinate, out var error))
            return FaraActionParseResult.Failed(rawResponse, error!);

        return FaraActionParseResult.Succeeded(new FaraAction(type, Coordinate: coordinate), rawResponse);
    }

    private static FaraActionParseResult DragAction(JsonElement value, string rawResponse)
    {
        if (!TryGetCoordinate(value, "coordinate", out var coordinate, out var error))
            return FaraActionParseResult.Failed(rawResponse, error!);

        TryGetCoordinate(value, "end_coordinate", out var endCoordinate, out _);
        if (endCoordinate is null)
            TryGetCoordinate(value, "endCoordinate", out endCoordinate, out _);

        return FaraActionParseResult.Succeeded(
            new FaraAction(FaraActionType.LeftClickDrag, coordinate, endCoordinate),
            rawResponse);
    }

    private static FaraActionParseResult StringAction(
        FaraActionType type,
        string propertyName,
        JsonElement value,
        string rawResponse)
    {
        var text = GetString(value, propertyName);
        return string.IsNullOrWhiteSpace(text)
            ? FaraActionParseResult.Failed(rawResponse, $"The {type} action requires a '{propertyName}' value.")
            : FaraActionParseResult.Succeeded(new FaraAction(type, Text: type == FaraActionType.Type ? text : null, Url: type == FaraActionType.VisitUrl ? text : null), rawResponse);
    }

    private static FaraActionParseResult KeyAction(JsonElement value, string rawResponse)
    {
        if (!value.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            return FaraActionParseResult.Failed(rawResponse, "The key action requires a 'keys' array.");

        var values = keys.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
        return values.Length == 0
            ? FaraActionParseResult.Failed(rawResponse, "The key action requires at least one key.")
            : FaraActionParseResult.Succeeded(new FaraAction(FaraActionType.Key, Keys: values), rawResponse);
    }

    private static FaraActionParseResult NumberAction(
        FaraActionType type,
        JsonElement value,
        string rawResponse)
    {
        if (!value.TryGetProperty("pixels", out var pixels) || !pixels.TryGetDouble(out var number) ||
            double.IsNaN(number) || double.IsInfinity(number))
        {
            return FaraActionParseResult.Failed(rawResponse, "The scroll action requires a numeric 'pixels' value.");
        }

        return FaraActionParseResult.Succeeded(new FaraAction(type, Pixels: number), rawResponse);
    }

    private static bool TryGetCoordinate(
        JsonElement value,
        string propertyName,
        out FaraCoordinate? coordinate,
        out string? error)
    {
        coordinate = null;
        error = null;
        if (!value.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array ||
            element.GetArrayLength() < 2 ||
            !element[0].TryGetInt32(out var x) ||
            !element[1].TryGetInt32(out var y))
        {
            error = $"The action requires a '{propertyName}' array containing integer x and y coordinates.";
            return false;
        }

        coordinate = new FaraCoordinate(x, y);
        return true;
    }

    private static JsonElement? FindActionObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        if (root.TryGetProperty("action", out _))
            return root;
        if (root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
            return arguments;
        if (root.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
            return parameters;
        return null;
    }

    private static string? GetString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Extracts the first complete JSON object from the response.
    /// </summary>
    /// <remarks>
    /// Fara frequently emits the same object repeatedly until it hits the token budget, so
    /// spanning from the first '{' to the last '}' would produce a concatenation of objects
    /// that is not valid JSON. Scanning for the first balanced object avoids that while still
    /// tolerating prose or code fences around it.
    /// </remarks>
    private static string? ExtractJson(string rawResponse)
    {
        var text = rawResponse.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            var endFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && endFence > firstLineEnd)
                text = text[(firstLineEnd + 1)..endFence].Trim();
        }

        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return text[start..(i + 1)];
                    break;
            }
        }

        return null;
    }
}
