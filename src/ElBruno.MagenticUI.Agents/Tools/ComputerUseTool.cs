using System.ComponentModel;
using System.Diagnostics;
using ElBruno.LocalLLMs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElBruno.MagenticUI.Agents.Tools;

public sealed class ComputerUseTool
{
    private static readonly ActivitySource ActivitySource = new("ElBruno.MagenticUI.ComputerUse");
    private readonly Func<CancellationToken, Task<LocalVisionChatClient>> _visionClientFactory;
    private readonly string _workingDirectory;
    private readonly ILogger<ComputerUseTool> _logger;

    public ComputerUseTool(
        Func<CancellationToken, Task<LocalVisionChatClient>> visionClientFactory,
        string workingDirectory,
        ILogger<ComputerUseTool>? logger = null)
    {
        _visionClientFactory = visionClientFactory;
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _logger = logger ?? NullLogger<ComputerUseTool>.Instance;
    }

    [Description("Analyzes an image file from the sandbox using the computer-use vision model and returns a concise answer.")]
    public async Task<string> DescribeImage(
        [Description("Relative path to the image file inside the sandbox")] string relativePath,
        [Description("What the computer-use model should answer about the image")] string prompt = "Describe what is visible in this image.",
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSandboxed(relativePath);
        if (!File.Exists(fullPath))
            return $"Error: image file not found: {relativePath}";

        using var activity = ActivitySource.StartActivity("computer.describe_image");
        activity?.SetTag("computer.image.path", relativePath);

        try
        {
            var visionClient = await _visionClientFactory(cancellationToken);
            var response = await visionClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, prompt)
            ],
            new VisionChatOptions
            {
                ImagePaths = [fullPath]
            },
            cancellationToken);

            var output = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(output))
                return "No output from computer-use model.";

            return output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Computer image analysis was cancelled for {RelativePath}", relativePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Computer image analysis failed for {RelativePath}", relativePath);
            return $"Error analyzing image '{relativePath}': {ex.Message}";
        }
    }

    private string ResolveSandboxed(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_workingDirectory, relativePath));
        if (!fullPath.StartsWith(_workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{relativePath}' resolves outside the working directory sandbox.");
        }

        return fullPath;
    }
}
