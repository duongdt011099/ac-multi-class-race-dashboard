using Bunit;
using multi_class_race_dashboard.Components;
using multi_class_race_dashboard.Components.Shared;

namespace MulticlassRace.Tests.Components;

public class NavItemTests : TestContext
{
    [Fact]
    public void Enabled_RendersLinkToHref()
    {
        var cut = RenderComponent<NavItem>(p => p
            .Add(n => n.Href, "teams")
            .Add(n => n.Icon, "bi-car-nav-menu")
            .Add(n => n.Text, "Teams"));

        var link = cut.Find("a.nav-link");
        Assert.EndsWith("teams", link.GetAttribute("href"));
        Assert.Contains("Teams", link.TextContent);
        Assert.Contains("bi-car-nav-menu", cut.Find("span.bi").ClassList);
    }

    [Fact]
    public void Disabled_RendersGreyedOutSpanWithoutLink()
    {
        var cut = RenderComponent<NavItem>(p => p
            .Add(n => n.Href, "teams")
            .Add(n => n.Icon, "bi-car-nav-menu")
            .Add(n => n.Text, "Teams")
            .Add(n => n.Enabled, false));

        Assert.Empty(cut.FindAll("a"));
        var disabled = cut.Find("span.wec-nav-disabled");
        Assert.Equal("true", disabled.GetAttribute("aria-disabled"));
        Assert.Contains("Teams", disabled.TextContent);
    }

}

public class UpdateNotificationHelperTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1000, "1000 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void FormatSize_PicksUnit(long bytes, string expected)
        => Assert.Equal(expected, UpdateNotification.FormatSize(bytes));

    [Fact]
    public void Truncate_ReturnsNullForBlankAndCutsLongNotes()
    {
        Assert.Null(UpdateNotification.Truncate(null));
        Assert.Null(UpdateNotification.Truncate("   "));
        Assert.Equal("short", UpdateNotification.Truncate("  short  "));

        var cut = UpdateNotification.Truncate(new string('a', 2000));

        Assert.Equal(1503, cut!.Length);
        Assert.EndsWith("...", cut);
    }
}
