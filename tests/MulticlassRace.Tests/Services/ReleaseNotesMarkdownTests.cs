using MulticlassRace.Services.Updates;

namespace MulticlassRace.Tests.Services;

public class ReleaseNotesMarkdownTests
{
    [Fact]
    public void ToHtml_FormatsHeadingsListsAndEmphasis()
    {
        var html = ReleaseNotesMarkdown.ToHtml("## What's new\n\n- **Safety car** fix\n- `code` sample\n");

        Assert.Contains("<h2", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<strong>Safety car</strong>", html);
        Assert.Contains("<code>code</code>", html);
    }

    [Fact]
    public void ToHtml_ShowsRawHtmlAsText()
    {
        var html = ReleaseNotesMarkdown.ToHtml("<script>alert(1)</script> <img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ToHtml_KeepsWebLinksAndDropsOtherSchemes()
    {
        var html = ReleaseNotesMarkdown.ToHtml(
            "[release](https://github.com/o/r/releases) [bad](javascript:alert(1)) ![pic](file:///C:/x.png)");

        Assert.Contains("href=\"https://github.com/o/r/releases\"", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("file:", html);
    }
}
