using System.Net;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using SaturdayPulse.Core.Content;
using SaturdayPulse.Services;

namespace SaturdayPulse.Controllers
{
    /// <summary>
    /// Public, anonymous, read-only HTML pages for the store listings
    /// (Privacy Policy URL, Terms, Support URL). Renders the same
    /// ApplicationContentDocument the mobile app shows, so the Admin console
    /// stays the single place the text is maintained.
    ///
    /// Deliberately NOT behind any auth: Apple and Google require these URLs
    /// to open without a login. No write endpoints live here - edits still go
    /// through PUT /api/content, which is gated by [AdminKey].
    ///
    /// Section bodies are admin-authored Markdown and/or HTML. Markdig passes
    /// raw HTML through, so both render. A restrictive Content-Security-Policy
    /// header blocks scripts as defense in depth.
    /// </summary>
    [ApiController]
    [Route("legal")]
    public class LegalController(
        ContentService contentService,
        ILogger<LegalController> logger) : ControllerBase
    {
        private static readonly MarkdownPipeline MarkdownPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        [HttpGet("privacy")]
        public Task<IActionResult> Privacy(CancellationToken token = default) =>
            RenderSectionAsync(doc => doc.PrivacyPolicy, "Privacy Policy", token);

        [HttpGet("terms")]
        public Task<IActionResult> Terms(CancellationToken token = default) =>
            RenderSectionAsync(doc => doc.TermsOfService, "Terms of Service", token);

        [HttpGet("support")]
        public async Task<IActionResult> Support(CancellationToken token = default)
        {
            try
            {
                ApplicationContentDocument doc = await contentService.GetContentAsync(token);

                var body = new System.Text.StringBuilder();

                string email = doc.SupportEmail?.Trim() ?? string.Empty;
                if (email.Length > 0)
                {
                    string safeEmail = WebUtility.HtmlEncode(email);
                    body.Append("<p>Questions or problems? Email us at <a href=\"mailto:")
                        .Append(safeEmail).Append("\">").Append(safeEmail).Append("</a>.</p>");
                }

                if (!string.IsNullOrWhiteSpace(doc.Faq.Content))
                {
                    string faqTitle = string.IsNullOrWhiteSpace(doc.Faq.Title)
                        ? "FAQ"
                        : doc.Faq.Title.Trim();
                    body.Append("<h2>").Append(WebUtility.HtmlEncode(faqTitle)).Append("</h2>");
                    body.Append(Markdown.ToHtml(doc.Faq.Content, MarkdownPipeline));
                }

                if (body.Length == 0)
                {
                    return StatusCode(404, "This page has not been published yet.");
                }

                return HtmlPage("Support", body.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error rendering legal support page");
                return StatusCode(500, "An error occurred while retrieving this page.");
            }
        }

        private async Task<IActionResult> RenderSectionAsync(
            Func<ApplicationContentDocument, ContentSection> selector,
            string fallbackTitle,
            CancellationToken token)
        {
            try
            {
                ApplicationContentDocument doc = await contentService.GetContentAsync(token);
                ContentSection section = selector(doc);

                if (string.IsNullOrWhiteSpace(section.Content))
                {
                    return StatusCode(404, "This page has not been published yet.");
                }

                string title = string.IsNullOrWhiteSpace(section.Title)
                    ? fallbackTitle
                    : section.Title.Trim();

                return HtmlPage(title, Markdown.ToHtml(section.Content, MarkdownPipeline));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error rendering legal page {Page}", fallbackTitle);
                return StatusCode(500, "An error occurred while retrieving this page.");
            }
        }

        private ContentResult HtmlPage(string title, string bodyHtml)
        {
            Response.Headers["Cache-Control"] = "public, max-age=300";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; style-src 'unsafe-inline'; img-src https: data:; base-uri 'none'; form-action 'none'";

            string safeTitle = WebUtility.HtmlEncode(title);

            string html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{safeTitle}} - J1S Sports</title>
<style>
  :root { color-scheme: light dark; }
  body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    line-height: 1.6;
    max-width: 760px;
    margin: 0 auto;
    padding: 24px 16px 64px;
    background: #ffffff;
    color: #1a1a1a;
  }
  h1 { font-size: 1.8rem; margin-bottom: 0.25em; }
  h2 { margin-top: 1.6em; }
  a { color: #0b5fff; }
  table { border-collapse: collapse; }
  td, th { border: 1px solid #999; padding: 4px 8px; }
  @media (prefers-color-scheme: dark) {
    body { background: #0d0d1a; color: #e8e8f0; }
    a { color: #7aa7ff; }
  }
</style>
</head>
<body>
<h1>{{safeTitle}}</h1>
{{bodyHtml}}
</body>
</html>
""";

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html; charset=utf-8",
                StatusCode = 200
            };
        }
    }
}
