using System.Net;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class MagenticUIOrchestratorTests
{
    [Fact]
    public async Task RunAsync_WhenStreamingNeverYields_FallsBackAfterTimeout()
    {
        // Arrange
        var chatClient = new HangingStreamingFallbackChatClient(
            [CreateTextResponse("""{"name":"Submit","arguments":{"result":"Recovered from stalled streaming."}}""")]);

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var fileSurfer = new FileSurferTool(workingDirectory);
            var webFetcher = new WebFetchTool(new HttpClient(new StubHttpMessageHandler("unused")));
            var codeExecutor = new CodeExecutorTool();
            var computerUseTool = new ComputerUseTool(
                _ => Task.FromException<ElBruno.LocalLLMs.LocalVisionChatClient>(new InvalidOperationException("Not expected.")),
                workingDirectory);
            var userProxy = new UserProxyAgent();
            var orchestrator = new MagenticUIOrchestrator(
                chatClient,
                fileSurfer,
                webFetcher,
                codeExecutor,
                computerUseTool,
                userProxy,
                maxRounds: 2,
                maxOutputTokens: 128,
                streamingFallbackTimeout: TimeSpan.FromMilliseconds(75),
                nonStreamingFallbackTimeout: TimeSpan.FromSeconds(2));

            var reportedMessages = new List<AgentMessage>();
            var progress = new SynchronousProgress<AgentMessage>(reportedMessages.Add);
            var request = new TaskRequest(Guid.NewGuid().ToString("N"), "Return a final answer.", workingDirectory);

            // Act
            await orchestrator.RunAsync(request, progress, CancellationToken.None);

            // Assert
            Assert.Equal(1, chatClient.StreamingCalls);
            Assert.Equal(1, chatClient.NonStreamingCalls);
            Assert.Contains(reportedMessages, message =>
                message.AgentName == "Orchestrator"
                && message.Role == "submit"
                && message.Text.Contains("Recovered from stalled streaming.", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenStreamingReturnsNoUpdates_FallsBackToNonStreamingResponse()
    {
        // Arrange
        var chatClient = new EmptyStreamingFallbackChatClient(
            [
                CreateTextResponse("""{"name":"WebFetcher_FetchUrl","arguments":{"url":"https://elbruno.com"}}"""),
                CreateTextResponse("""{"name":"Submit","arguments":{"result":"Fallback path completed."}}""")
            ]);

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var fileSurfer = new FileSurferTool(workingDirectory);
            var webFetcher = new WebFetchTool(new HttpClient(new StubHttpMessageHandler("El Bruno page content.")));
            var codeExecutor = new CodeExecutorTool();
            var computerUseTool = new ComputerUseTool(
                _ => Task.FromException<ElBruno.LocalLLMs.LocalVisionChatClient>(new InvalidOperationException("Not expected.")),
                workingDirectory);
            var userProxy = new UserProxyAgent();
            var orchestrator = new MagenticUIOrchestrator(
                chatClient,
                fileSurfer,
                webFetcher,
                codeExecutor,
                computerUseTool,
                userProxy,
                maxRounds: 4,
                maxOutputTokens: 128);

            var reportedMessages = new List<AgentMessage>();
            var progress = new SynchronousProgress<AgentMessage>(reportedMessages.Add);
            var request = new TaskRequest(Guid.NewGuid().ToString("N"), "Please fetch https://elbruno.com", workingDirectory);

            // Act
            await orchestrator.RunAsync(request, progress, CancellationToken.None);

            // Assert
            Assert.Equal(2, chatClient.NonStreamingCalls);
            Assert.Equal(2, chatClient.StreamingCalls);
            Assert.Contains(reportedMessages, message =>
                message.AgentName == "Orchestrator"
                && message.Role == "submit"
                && message.Text.Contains("Fallback path completed.", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenResponseStreamsInSmallChunks_ReportsIncrementalAssistantStreamUpdates()
    {
        // Arrange
        const string finalAnswer = "El Bruno writes about local AI and .NET tooling.";
        var chatClient = new ChunkedRecordingChatClient([CreateTextResponse(finalAnswer)], chunkSize: 8);

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var fileSurfer = new FileSurferTool(workingDirectory);
            var webFetcher = new WebFetchTool(new HttpClient(new StubHttpMessageHandler("unused")));
            var codeExecutor = new CodeExecutorTool();
            var computerUseTool = new ComputerUseTool(
                _ => Task.FromException<ElBruno.LocalLLMs.LocalVisionChatClient>(new InvalidOperationException("Not expected.")),
                workingDirectory);
            var userProxy = new UserProxyAgent();
            var orchestrator = new MagenticUIOrchestrator(
                chatClient,
                fileSurfer,
                webFetcher,
                codeExecutor,
                computerUseTool,
                userProxy,
                maxRounds: 3,
                maxOutputTokens: 128);

            var reportedMessages = new List<AgentMessage>();
            var progress = new SynchronousProgress<AgentMessage>(reportedMessages.Add);
            var request = new TaskRequest(Guid.NewGuid().ToString("N"), "Give me a short answer.", workingDirectory);

            // Act
            await orchestrator.RunAsync(request, progress, CancellationToken.None);

            // Assert
            var streamMessages = reportedMessages
                .Where(message => message.AgentName == "Orchestrator" && message.Role == "assistant_stream")
                .ToList();

            Assert.True(streamMessages.Count >= 2, "Expected at least two incremental assistant_stream updates.");
            Assert.True(streamMessages[0].Text.Length < streamMessages[^1].Text.Length);
            Assert.Equal(finalAnswer, streamMessages[^1].Text);
            Assert.Contains(reportedMessages, message =>
                message.AgentName == "Orchestrator"
                && message.Role == "assistant"
                && message.Text == finalAnswer);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WebTask_PassesMaxOutputTokens_AndDoesNotInitializeComputerUse()
    {
        // Arrange
        var chatClient = new RecordingChatClient(
        [
            CreateTextResponse("""{"name":"WebFetcher_FetchUrl","arguments":{"url":"https://elbruno.com"}}"""),
            CreateTextResponse("""{"name":"Submit","arguments":{"result":"El Bruno writes about local AI."}}""")
        ]);

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var fileSurfer = new FileSurferTool(workingDirectory);
            var webFetcher = new WebFetchTool(new HttpClient(new StubHttpMessageHandler("El Bruno writes about local AI and .NET.")));
            var codeExecutor = new CodeExecutorTool();
            var computerUseFactoryCalls = 0;
            var computerUseTool = new ComputerUseTool(
                _ =>
                {
                    computerUseFactoryCalls++;
                    return Task.FromException<ElBruno.LocalLLMs.LocalVisionChatClient>(
                        new InvalidOperationException("Computer-use model should not be initialized for a web-only task."));
                },
                workingDirectory);
            var userProxy = new UserProxyAgent();
            var orchestrator = new MagenticUIOrchestrator(
                chatClient,
                fileSurfer,
                webFetcher,
                codeExecutor,
                computerUseTool,
                userProxy,
                maxRounds: 4,
                maxOutputTokens: 128);

            var reportedMessages = new List<AgentMessage>();
            var progress = new SynchronousProgress<AgentMessage>(reportedMessages.Add);
            var request = new TaskRequest(Guid.NewGuid().ToString("N"), "Please fetch https://elbruno.com", workingDirectory);

            // Act
            await orchestrator.RunAsync(request, progress, CancellationToken.None);

            // Assert
            Assert.Equal(2, chatClient.RecordedOptions.Count);
            Assert.All(chatClient.RecordedOptions, options => Assert.Equal(128, options.MaxOutputTokens));
            Assert.Contains(reportedMessages, message => message.AgentName == "Orchestrator" && message.Role == "tool" && message.Text.Contains("WebFetcher_FetchUrl", StringComparison.Ordinal));
            Assert.Contains(reportedMessages, message => message.AgentName == "Orchestrator" && message.Role == "submit" && message.Text.Contains("El Bruno writes about local AI.", StringComparison.Ordinal));
            Assert.Equal(0, computerUseFactoryCalls);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

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

    private static ChatResponse CreateTextResponse(string text)
        => new(new ChatMessage(ChatRole.Assistant, text));

    private sealed class RecordingChatClient(IReadOnlyList<ChatResponse> responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public List<ChatOptions> RecordedOptions { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Tests should exercise the streaming orchestrator path.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more chat responses were configured for the test.");

            RecordedOptions.Add(options ?? new ChatOptions());
            var response = _responses.Dequeue();

            foreach (var message in response.Messages)
            {
                if (!string.IsNullOrEmpty(message.Text))
                    yield return new ChatResponseUpdate(ChatRole.Assistant, message.Text);

                var nonTextContents = message.Contents.Where(content => content is not TextContent).ToList();
                if (nonTextContents.Count > 0)
                    yield return new ChatResponseUpdate(ChatRole.Assistant, nonTextContents);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class ChunkedRecordingChatClient(IReadOnlyList<ChatResponse> responses, int chunkSize) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);
        private readonly int _chunkSize = Math.Max(1, chunkSize);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Tests should exercise the streaming orchestrator path.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more chat responses were configured for the test.");

            var response = _responses.Dequeue();

            foreach (var message in response.Messages)
            {
                if (!string.IsNullOrEmpty(message.Text))
                {
                    for (var index = 0; index < message.Text.Length; index += _chunkSize)
                    {
                        var length = Math.Min(_chunkSize, message.Text.Length - index);
                        var chunk = message.Text.Substring(index, length);
                        yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                    }
                }

                var nonTextContents = message.Contents.Where(content => content is not TextContent).ToList();
                if (nonTextContents.Count > 0)
                    yield return new ChatResponseUpdate(ChatRole.Assistant, nonTextContents);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
    }

    private sealed class EmptyStreamingFallbackChatClient(IReadOnlyList<ChatResponse> responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public int StreamingCalls { get; private set; }
        public int NonStreamingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NonStreamingCalls++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more fallback responses were configured for the test.");

            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class HangingStreamingFallbackChatClient(IReadOnlyList<ChatResponse> responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public int StreamingCalls { get; private set; }
        public int NonStreamingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            NonStreamingCalls++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more fallback responses were configured for the test.");

            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            await Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}