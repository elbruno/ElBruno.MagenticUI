using System.ComponentModel;
using ElBruno.MarkItDotNet;

namespace ElBruno.MagenticUI.Agents.Tools;

public sealed class MarkItDownTool
{
    private readonly IMarkdownConverter _converter;

    public MarkItDownTool(IMarkdownConverter converter)
    {
        _converter = converter;
    }

    [Description("Converts a document file (PDF, DOCX, XLSX, PPTX, HTML, etc.) to Markdown text.")]
    public async Task<string> ConvertToMarkdown(
        [Description("Absolute or relative path to the document file")] string filePath)
    {
        if (!File.Exists(filePath))
            return $"Error: file not found: {filePath}";

        try
        {
            var markdown = await _converter.ConvertAsync(filePath);
            return markdown ?? string.Empty;
        }
        catch (Exception ex)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch
            {
                return $"Error converting file '{filePath}': {ex.Message}";
            }
        }
    }
}
