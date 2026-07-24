using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;
using ElBruno.MarkItDotNet;

namespace ElBruno.MagenticUI.Agents.Tools;

public sealed class WebFetchTool
{
    private const int MaxContentCharacters = 4000;
    private readonly HttpClient _httpClient;
    private readonly IMarkdownConverter? _markdownConverter;

    public WebFetchTool(HttpClient httpClient, IMarkdownConverter? markdownConverter = null)
    {
        _httpClient = httpClient;
        _markdownConverter = markdownConverter;
    }

    [Description("Fetches the content of a URL. Returns Markdown if the page is HTML and a converter is configured, otherwise plain text (truncated to 4000 characters).")]
    public async Task<string> FetchUrl(
        [Description("The fully-qualified URL to fetch (must start with http:// or https://)")] string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"Error: invalid URL '{url}'. Must start with http:// or https://.";
        }

        try
        {
            using var response = await _httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var content = await response.Content.ReadAsStringAsync();

            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                _markdownConverter is not null)
            {
                var markdown = await _markdownConverter.ConvertAsync(url);
                return Truncate(markdown);
            }

            var text = IsHtml(contentType)
                ? StripHtmlTags(content)
                : content;

            return Truncate(text);
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching '{url}': {ex.Message}";
        }
    }

    private static bool IsHtml(string contentType) =>
        contentType.Contains("html", StringComparison.OrdinalIgnoreCase);

    private static string StripHtmlTags(string html)
    {
        var withoutNonContent = Regex.Replace(
            html,
            "<(script|style|noscript)[^>]*>[\\s\\S]*?</\\1>",
            " ",
            RegexOptions.IgnoreCase);
        var text = Regex.Replace(withoutNonContent, "<[^>]*>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
    }

    private static string Truncate(string text) =>
        text.Length > MaxContentCharacters
            ? text[..MaxContentCharacters] + "\n...[truncated]"
            : text;
}
