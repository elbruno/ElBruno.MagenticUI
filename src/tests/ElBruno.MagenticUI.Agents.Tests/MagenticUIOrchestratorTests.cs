using ElBruno.MagenticUI.Agents.Orchestrator;
using System.Text.Json;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class MagenticUIOrchestratorTests
{
    [Fact]
    public void TryParseTextToolCall_WhenResponseContainsMultipleObjects_ParsesFirstCall()
    {
        // Arrange
        const string response =
            """
            {"name": "WebFetcher_FetchUrl", "arguments": {"url": "https://elbruno.com"}}

            {"name": "Submit", "arguments": {"result": "The provided link is not accessible."}}}}
            """;

        // Act
        var call = MagenticUIOrchestrator.TryParseTextToolCall(response);

        // Assert
        Assert.NotNull(call);
        Assert.Equal("WebFetcher_FetchUrl", call.Name);
        Assert.Equal("https://elbruno.com", call.Arguments!["url"]);
    }

    [Fact]
    public void TryParseTextToolCalls_WhenResponseContainsMultipleCalls_ParsesAllCalls()
    {
        // Arrange
        const string response =
            """
            {"name":"FileSurfer_ReadFile","arguments":{"relativePath":"invoice1.txt"}}
            {"name":"FileSurfer_ReadFile","arguments":{"relativePath":"invoice2.txt"}}
            """;

        // Act
        var calls = MagenticUIOrchestrator.TryParseTextToolCalls(response);

        // Assert
        Assert.Equal(2, calls.Count);
        Assert.Equal("invoice1.txt", calls[0].Arguments!["relativePath"]);
        Assert.Equal("invoice2.txt", calls[1].Arguments!["relativePath"]);
    }

    [Fact]
    public void SelectNextTextToolCall_WhenSubmitAndToolArePresent_SelectsOnlyTool()
    {
        // Arrange
        var calls = MagenticUIOrchestrator.TryParseTextToolCalls(
            """
            {"name":"Submit","arguments":{"result":"Premature answer"}}
            {"name":"WebFetcher_FetchUrl","arguments":{"url":"https://elbruno.com"}}
            {"name":"Coder_ExecuteCode","arguments":{"code":"summarize(content)"}}
            """);

        // Act
        var selected = MagenticUIOrchestrator.SelectNextTextToolCall(calls);

        // Assert
        Assert.Equal("WebFetcher_FetchUrl", selected.Name);
    }

    [Fact]
    public void TruncateToolResult_WhenResultExceedsModelBudget_TruncatesIt()
    {
        // Arrange
        var result = new string('x', 1500);

        // Act
        var truncated = MagenticUIOrchestrator.TruncateToolResult(result);

        // Assert
        Assert.Equal(414, truncated.Length);
        Assert.EndsWith("...[truncated]", truncated);
    }

    [Fact]
    public void TryExtractSubmitResult_WhenSubmitFollowsAnotherCall_ReturnsFinalResult()
    {
        // Arrange
        const string response =
            """
            {"name":"Coder_ExecuteCode","arguments":{"code":"print('summary')"}}
            {"name":"Submit","arguments":{"result":"El Bruno covers local AI, .NET, and agent workflows."}}
            """;

        // Act
        var found = MagenticUIOrchestrator.TryExtractSubmitResult(response, out var result);

        // Assert
        Assert.True(found);
        Assert.Equal("El Bruno covers local AI, .NET, and agent workflows.", result);
    }

    [Fact]
    public void TryExtractSubmitResult_WhenResultIsAnExpression_RejectsPartialString()
    {
        // Arrange
        const string response =
            """
            {"name":"Submit","arguments":{"result":"The total is " + total_amount}}
            """;

        // Act
        var found = MagenticUIOrchestrator.TryExtractSubmitResult(response, out _);

        // Assert
        Assert.False(found);
    }

    [Fact]
    public void FormatTextToolResults_WhenMultipleToolsRun_PreservesToolNames()
    {
        // Arrange
        (string ToolName, string Result)[] results =
        [
            ("WebFetcher_FetchUrl", "Page content"),
            ("Coder_ExecuteCode", "Computed summary")
        ];

        // Act
        var formatted = MagenticUIOrchestrator.FormatTextToolResults(results);

        // Assert
        Assert.Contains("Result from WebFetcher_FetchUrl:\nPage content", formatted);
        Assert.Contains("Result from Coder_ExecuteCode:\nComputed summary", formatted);
    }

    [Theory]
    [InlineData("Do not repeat WebFetcher_FetchUrl with the same arguments.")]
    [InlineData("Use this output, then call another needed tool or Submit the final answer.")]
    [InlineData("")]
    public void IsInvalidSubmitResult_WhenResultContainsControlInstructions_RejectsIt(string result)
    {
        // Act
        var invalid = MagenticUIOrchestrator.IsInvalidSubmitResult(result);

        // Assert
        Assert.True(invalid);
    }

    [Fact]
    public void IsInvalidSubmitResult_WhenResultIsARealAnswer_AcceptsIt()
    {
        // Act
        var invalid = MagenticUIOrchestrator.IsInvalidSubmitResult(
            "El Bruno writes about local AI, .NET, and developer tools.");

        // Assert
        Assert.False(invalid);
    }

    [Fact]
    public void TryGetRelativePath_WhenArgsContainRelativePath_ReturnsValue()
    {
        // Arrange
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["relativePath"] = "captures/step1.png"
        };

        // Act
        var found = MagenticUIOrchestrator.TryGetRelativePath(args, out var relativePath);

        // Assert
        Assert.True(found);
        Assert.Equal("captures/step1.png", relativePath);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("imagePath")]
    [InlineData("image_path")]
    [InlineData("RelativePath")]
    public void TryGetRelativePath_WhenArgsUseSupportedAliases_ReturnsValue(string key)
    {
        // Arrange
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            [key] = "captures/step2.png"
        };

        // Act
        var found = MagenticUIOrchestrator.TryGetRelativePath(args, out var relativePath);

        // Assert
        Assert.True(found);
        Assert.Equal("captures/step2.png", relativePath);
    }

    [Fact]
    public void TryGetRelativePath_WhenValueIsJsonElementString_ReturnsValue()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"relativePath":"captures/step3.png"}""");
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["relativePath"] = document.RootElement.GetProperty("relativePath")
        };

        // Act
        var found = MagenticUIOrchestrator.TryGetRelativePath(args, out var relativePath);

        // Assert
        Assert.True(found);
        Assert.Equal("captures/step3.png", relativePath);
    }

    [Fact]
    public void TryGetRelativePath_WhenQuotedStringValue_TrimsAndUnquotes()
    {
        // Arrange
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["relativePath"] = "\"captures/step4.png\""
        };

        // Act
        var found = MagenticUIOrchestrator.TryGetRelativePath(args, out var relativePath);

        // Assert
        Assert.True(found);
        Assert.Equal("captures/step4.png", relativePath);
    }

    [Fact]
    public void TryGetRelativePath_WhenMissingPathKeys_ReturnsFalse()
    {
        // Arrange
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["prompt"] = "describe this screenshot"
        };

        // Act
        var found = MagenticUIOrchestrator.TryGetRelativePath(args, out var relativePath);

        // Assert
        Assert.False(found);
        Assert.Equal(string.Empty, relativePath);
    }

    [Fact]
    public void TryBuildImageDataUri_WhenImageExists_ReturnsDataUri()
    {
        // Arrange
        var outputDir = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(outputDir);
        var imagePath = Path.Combine(outputDir, "pixel.png");
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8Xw8AArgB9VPfLSEAAAAASUVORK5CYII=");
        File.WriteAllBytes(imagePath, pngBytes);

        try
        {
            // Act
            var created = MagenticUIOrchestrator.TryBuildImageDataUri(imagePath, out var dataUri);

            // Assert
            Assert.True(created);
            Assert.StartsWith("data:image/png;base64,", dataUri, StringComparison.Ordinal);
            Assert.Contains(Convert.ToBase64String(pngBytes), dataUri, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void TryBuildImageDataUri_WhenExtensionIsUnsupported_ReturnsFalse()
    {
        // Arrange
        var outputDir = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(outputDir);
        var imagePath = Path.Combine(outputDir, "pixel.bmp");
        File.WriteAllBytes(imagePath, [0x42, 0x4D, 0x00]);

        try
        {
            // Act
            var created = MagenticUIOrchestrator.TryBuildImageDataUri(imagePath, out var dataUri);

            // Assert
            Assert.False(created);
            Assert.Equal(string.Empty, dataUri);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Theory]
    [InlineData("Computer_DescribeImage", true)]
    [InlineData("Computer_Click", true)]
    [InlineData("WebFetcher_FetchUrl", false)]
    [InlineData("", false)]
    public void IsComputerAction_ReturnsExpectedResult(string toolName, bool expected)
    {
        // Act
        var isComputerAction = MagenticUIOrchestrator.IsComputerAction(toolName);

        // Assert
        Assert.Equal(expected, isComputerAction);
    }

    [Theory]
    [InlineData("Computer_DescribeImage", true)]
    [InlineData("Computer_Click", false)]
    [InlineData("computer_describeimage", false)]
    [InlineData("", false)]
    public void IsComputerScreenshotAction_ReturnsExpectedResult(string toolName, bool expected)
    {
        // Act
        var isScreenshotAction = MagenticUIOrchestrator.IsComputerScreenshotAction(toolName);

        // Assert
        Assert.Equal(expected, isScreenshotAction);
    }
}