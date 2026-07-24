using ElBruno.MagenticUI.Agents.Orchestrator;

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
}