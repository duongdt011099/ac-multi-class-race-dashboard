using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using multi_class_race_dashboard.Components.Shared;
using MulticlassRace.Common;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Components;

public class SharedComponentTests : TestContext
{
    private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);

    [Fact]
    public void Modal_RendersTitleBodyAndSizeClass()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(m => m.Title, "Edit Team")
            .Add(m => m.Size, ModalSize.Wide)
            .Add(m => m.ChildContent, Text("body")));

        Assert.Equal("Edit Team", cut.Find(".wec-modal__header span").TextContent);
        Assert.Contains("wec-modal--wide", cut.Find(".wec-modal").ClassList);
        Assert.Equal("body", cut.Find(".wec-modal__body").TextContent.Trim());
    }

    [Fact]
    public void Modal_WithoutFooterOrClose_OmitsThem()
    {
        var cut = RenderComponent<Modal>(p => p.Add(m => m.Title, "T"));

        Assert.Empty(cut.FindAll(".wec-modal__footer"));
        Assert.Empty(cut.FindAll(".wec-modal__close"));
    }

    [Fact]
    public void Modal_CloseButton_InvokesOnClose()
    {
        var closed = false;
        var cut = RenderComponent<Modal>(p => p
            .Add(m => m.Title, "T")
            .Add(m => m.OnClose, () => closed = true));

        cut.Find(".wec-modal__close").Click();

        Assert.True(closed);
    }

    [Fact]
    public void Modal_AppliesOverlayClass()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(m => m.Title, "T")
            .Add(m => m.OverlayClass, "wec-modal-overlay--import"));

        Assert.Contains("wec-modal-overlay--import", cut.Find(".wec-modal-overlay").ClassList);
    }

    [Fact]
    public void ModalActions_ShowsBusyTextAndDisablesConfirm()
    {
        var cut = RenderComponent<ModalActions>(p => p
            .Add(a => a.ConfirmText, "Save")
            .Add(a => a.BusyText, "Saving...")
            .Add(a => a.IsBusy, true));

        var confirm = cut.Find(".wec-modal__btn--primary");
        Assert.Equal("Saving...", confirm.TextContent.Trim());
        Assert.True(confirm.HasAttribute("disabled"));
    }

    [Fact]
    public void ModalActions_DangerUsesDangerStyle()
    {
        var cut = RenderComponent<ModalActions>(p => p
            .Add(a => a.ConfirmText, "Delete")
            .Add(a => a.Danger, true));

        Assert.Single(cut.FindAll(".wec-modal__btn--danger"));
        Assert.Empty(cut.FindAll(".wec-modal__btn--primary"));
    }

    [Fact]
    public void ConfirmDialog_ConfirmAndCancelRaiseCallbacks()
    {
        var confirmed = 0;
        var cancelled = 0;
        var cut = RenderComponent<ConfirmDialog>(p => p
            .Add(d => d.Title, "Delete Season")
            .Add(d => d.ChildContent, Text("Sure?"))
            .Add(d => d.OnConfirm, () => confirmed++)
            .Add(d => d.OnCancel, () => cancelled++));

        cut.Find(".wec-modal__btn--danger").Click();
        cut.Find(".wec-modal__btn--secondary").Click();

        Assert.Equal(1, confirmed);
        Assert.Equal(1, cancelled);
        Assert.Contains("wec-modal--small", cut.Find(".wec-modal").ClassList);
        Assert.Contains("Sure?", cut.Find(".wec-modal__body p").TextContent);
    }

    [Fact]
    public void AppSelect_RendersOptionsAndEmptyLabel()
    {
        var cut = RenderComponent<AppSelect>(p => p
            .Add(s => s.EmptyLabel, "All classes")
            .Add(s => s.Options, new[] { new SelectOption("a", "Alpha"), new SelectOption("b", "Beta") }));

        var labels = cut.FindAll("option").Select(o => o.TextContent).ToArray();
        Assert.Equal(new[] { "All classes", "Alpha", "Beta" }, labels);
    }

    [Fact]
    public void AppSelect_ShowsOrphanLabelForUnknownValue()
    {
        var cut = RenderComponent<AppSelect>(p => p
            .Add(s => s.Value, "gone")
            .Add(s => s.OrphanLabel, "gone (not found on disk)")
            .Add(s => s.Options, new[] { new SelectOption("a", "Alpha") }));

        Assert.Contains(cut.FindAll("option"), o => o.TextContent == "gone (not found on disk)");
    }

    [Fact]
    public void AppSelect_Change_RaisesValueChanged()
    {
        string? value = null;
        var cut = RenderComponent<AppSelect>(p => p
            .Add(s => s.Options, new[] { new SelectOption("a", "Alpha"), new SelectOption("b", "Beta") })
            .Add(s => s.ValueChanged, v => value = v));

        cut.Find("select").Change("b");

        Assert.Equal("b", value);
    }

    [Fact]
    public void SortableHeader_MarksActiveColumnAndDirection()
    {
        var active = RenderComponent<SortableHeader<string>>(p => p
            .Add(h => h.Column, "Team")
            .Add(h => h.Current, "Team")
            .Add(h => h.Descending, true)
            .Add(h => h.ChildContent, Text("Team")));
        var inactive = RenderComponent<SortableHeader<string>>(p => p
            .Add(h => h.Column, "Class")
            .Add(h => h.Current, "Team")
            .Add(h => h.ChildContent, Text("Class")));

        Assert.Contains("bi-arrow-down-short", active.Find("i").ClassList);
        Assert.DoesNotContain("wec-table__sort-icon--inactive", active.Find("i").ClassList);
        Assert.Contains("wec-table__sort-icon--inactive", inactive.Find("i").ClassList);
    }

    [Fact]
    public void SortableHeader_Click_RaisesOnSortWithColumn()
    {
        string? sorted = null;
        var cut = RenderComponent<SortableHeader<string>>(p => p
            .Add(h => h.Column, "Status")
            .Add(h => h.OnSort, c => sorted = c));

        cut.Find("button").Click();

        Assert.Equal("Status", sorted);
    }

    [Fact]
    public void Pagination_HiddenWhenSinglePage()
    {
        var cut = RenderComponent<Pagination>(p => p.Add(x => x.TotalPages, 1));

        Assert.Empty(cut.FindAll(".wec-pagination"));
    }

    [Fact]
    public void Pagination_NavigatesAndDisablesAtEdges()
    {
        var page = 0;
        var cut = RenderComponent<Pagination>(p => p
            .Add(x => x.Page, 1)
            .Add(x => x.TotalPages, 3)
            .Add(x => x.PageChanged, v => page = v));

        var buttons = cut.FindAll("button");
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.Contains("Page 1 of 3", cut.Find(".wec-pagination__info").TextContent);

        buttons[1].Click();
        Assert.Equal(2, page);
    }

    [Fact]
    public void DataTable_EmptyShowsMessageSpanningColumns()
    {
        var cut = RenderComponent<DataTable>(p => p
            .Add(t => t.IsEmpty, true)
            .Add(t => t.ColumnCount, 5)
            .Add(t => t.EmptyText, "No teams found")
            .Add(t => t.Header, Text("h"))
            .Add(t => t.Body, Text("b")));

        var cell = cut.Find("td.wec-empty");
        Assert.Equal("5", cell.GetAttribute("colspan"));
        Assert.Equal("No teams found", cell.TextContent);
    }

    [Fact]
    public void DataTable_NonEmptyRendersBody()
    {
        var cut = RenderComponent<DataTable>(p => p
            .Add(t => t.Header, builder =>
            {
                builder.OpenElement(0, "th");
                builder.AddContent(1, "head");
                builder.CloseElement();
            })
            .Add(t => t.Body, builder =>
            {
                builder.OpenElement(0, "tr");
                builder.OpenElement(1, "td");
                builder.AddContent(2, "row");
                builder.CloseElement();
                builder.CloseElement();
            }));

        Assert.Empty(cut.FindAll("td.wec-empty"));
        Assert.Equal("head", cut.Find("thead th").TextContent);
        Assert.Equal("row", cut.Find("tbody td").TextContent);
    }

    [Theory]
    [InlineData(true, "Active", false)]
    [InlineData(false, "Inactive", true)]
    public void StatusBadge_RendersState(bool isActive, string text, bool inactiveClass)
    {
        var cut = RenderComponent<StatusBadge>(p => p.Add(b => b.IsActive, isActive));

        var span = cut.Find("span.wec-status");
        Assert.Equal(text, span.TextContent.Trim());
        Assert.Equal(inactiveClass, span.ClassList.Contains("wec-status--inactive"));
    }

    [Fact]
    public void ClassBadges_EmptyShowsDash()
    {
        var cut = RenderComponent<ClassBadges>();

        Assert.Equal("—", cut.Find(".wec-table__classes span").TextContent);
    }

    [Fact]
    public void ClassBadges_RendersColoredBadges()
    {
        var tags = new List<ClassTagModel>
        {
            new() { ItemName = "GT3", SettingName = "WEC", Color = "#3fa34d" }
        };

        var cut = RenderComponent<ClassBadges>(p => p.Add(b => b.Tags, tags));

        var badge = cut.Find(".wec-badge");
        Assert.Equal("GT3", badge.TextContent);
        Assert.Equal("WEC", badge.GetAttribute("title"));
        Assert.Contains("#3fa34d", badge.GetAttribute("style"));
    }

    [Fact]
    public void TeamCell_ShowsLogoWhenPresent()
    {
        var cut = RenderComponent<TeamCell>(p => p
            .Add(c => c.Name, "Ferrari")
            .Add(c => c.Logo, "/logo.png"));

        Assert.Equal("/logo.png", cut.Find("img.wec-table__logo").GetAttribute("src"));
    }

    [Fact]
    public void TeamCell_FallbackShowsInitialsOnlyWhenEnabled()
    {
        var withFallback = RenderComponent<TeamCell>(p => p
            .Add(c => c.Name, "Ferrari")
            .Add(c => c.ShowInitialsFallback, true));
        var without = RenderComponent<TeamCell>(p => p.Add(c => c.Name, "Ferrari"));

        Assert.Equal("FE", withFallback.Find(".wec-table__logo--fallback").TextContent);
        Assert.Empty(without.FindAll(".wec-table__logo"));
    }

    [Fact]
    public void IconButton_RaisesClick()
    {
        var clicks = 0;
        var cut = RenderComponent<IconButton>(p => p
            .Add(b => b.Icon, "bi-pencil")
            .Add(b => b.Title, "Edit")
            .Add(b => b.OnClick, (MouseEventArgs _) => clicks++));

        cut.Find("button.wec-btn").Click();

        Assert.Equal(1, clicks);
        Assert.Contains("bi-pencil", cut.Find("i").ClassList);
    }

    [Fact]
    public void FormField_LinksLabelAndShowsHint()
    {
        var cut = RenderComponent<FormField>(p => p
            .Add(f => f.For, "team-name")
            .Add(f => f.Label, "Team Name")
            .Add(f => f.Hint, "Required")
            .Add(f => f.ChildContent, Text("input")));

        Assert.Equal("team-name", cut.Find("label").GetAttribute("for"));
        Assert.Equal("Required", cut.Find(".wec-form-hint").TextContent);
    }

    [Fact]
    public void CheckboxField_Change_RaisesValueChanged()
    {
        bool? value = null;
        var cut = RenderComponent<CheckboxField>(p => p
            .Add(c => c.Id, "active")
            .Add(c => c.Label, "Is Active")
            .Add(c => c.ValueChanged, v => value = v));

        cut.Find("input").Change(true);

        Assert.True(value);
    }

    [Fact]
    public void SearchFilter_Input_RaisesValueChanged()
    {
        string? value = null;
        var cut = RenderComponent<SearchFilter>(p => p
            .Add(s => s.Id, "q")
            .Add(s => s.ValueChanged, v => value = v));

        cut.Find("input").Input("abc");

        Assert.Equal("abc", value);
    }

    [Fact]
    public void ToolbarButton_DisabledAddsModifier()
    {
        var cut = RenderComponent<ToolbarButton>(p => p
            .Add(b => b.Disabled, true)
            .Add(b => b.ChildContent, Text("Import")));

        Assert.Contains("wec-toolbar__btn--disabled", cut.Find("button").ClassList);
    }
}
