using ElBruno.MagenticUI.Agents.Tools;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class FileSurferToolTests
{
    [Fact]
    public void CreateDirectory_CreatesFolderInsideSandbox()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var tool = new FileSurferTool(workingDirectory);

            tool.CreateDirectory("notes");

            Assert.True(Directory.Exists(Path.Combine(workingDirectory, "notes")));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void MoveFile_MovesFileWithinSandbox()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var sourceRelativePath = "notes_q1.txt";
            var destinationRelativePath = "notes/notes_q1.txt";
            var sourcePath = Path.Combine(workingDirectory, sourceRelativePath);
            File.WriteAllText(sourcePath, "Q1 notes");

            var tool = new FileSurferTool(workingDirectory);

            tool.MoveFile(sourceRelativePath, destinationRelativePath);

            Assert.False(File.Exists(sourcePath));
            Assert.True(File.Exists(Path.Combine(workingDirectory, destinationRelativePath)));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void MoveFile_WhenSourceIsMissing_ThrowsFileNotFoundException()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"magentic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var tool = new FileSurferTool(workingDirectory);

            var exception = Assert.Throws<FileNotFoundException>(() =>
                tool.MoveFile("missing.txt", "archive/missing.txt"));

            Assert.Contains("Source file not found", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
