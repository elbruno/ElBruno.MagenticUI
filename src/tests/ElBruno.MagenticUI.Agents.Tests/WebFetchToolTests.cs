using System.Net;
using ElBruno.MagenticUI.Agents.Tools;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class WebFetchToolTests
{
    [Fact]
    public async Task FetchUrl_WhenHtmlContainsLargeScripts_ReturnsBoundedPageText()
    {
        // Arrange
        var script = new string('x', 5000);
        var html = $"<html><head><style>{script}</style><script>{script}</script></head>" +
            "<body><h1>El Bruno</h1><p>Dev Advocating &amp; AI engineering.</p></body></html>";
        using var httpClient = new HttpClient(new StubHttpMessageHandler(html));
        var tool = new WebFetchTool(httpClient);

        // Act
        var result = await tool.FetchUrl("https://example.com");

        // Assert
        Assert.Equal("El Bruno Dev Advocating & AI engineering.", result);
        Assert.DoesNotContain(script, result);
        Assert.True(result.Length <= 4015);
    }

    private sealed class StubHttpMessageHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
            });
    }
}