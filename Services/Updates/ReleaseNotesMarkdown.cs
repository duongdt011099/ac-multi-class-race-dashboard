using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MulticlassRace.Services.Updates;

/// <summary>
/// Turns GitHub release notes, which are written in Markdown, into HTML for the update dialog.
/// The notes come from the network, so raw HTML in them is shown as text and any link or image that
/// is not plain http(s) loses its target; nothing in a release body can run script in the dashboard.
/// </summary>
public static class ReleaseNotesMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsWebAddress(link.Url))
            {
                link.Url = null;
            }
        }

        foreach (var autoLink in document.Descendants<AutolinkInline>())
        {
            if (autoLink.IsEmail || !IsWebAddress(autoLink.Url))
            {
                autoLink.Url = string.Empty;
            }
        }

        return document.ToHtml(Pipeline);
    }

    private static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
