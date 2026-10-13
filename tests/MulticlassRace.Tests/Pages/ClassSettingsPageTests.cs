using Bunit;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Services;
using MulticlassRace.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MulticlassRace.Tests.Pages;

public class ClassSettingsPageTests : PageTestContext
{
    [Fact]
    public void Renders_SeededSettingWithClassBadges()
    {
        var cut = RenderComponent<ClassSettings>();

        cut.WaitForAssertion(() => Assert.Contains("WEC", cut.Markup));
        Assert.Equal(new[] { "Hypercar", "LMP2", "GT3" }, cut.FindAll(".wec-badge").Select(b => b.TextContent));
        Assert.Empty(cut.FindAll("button[title^='Delete']"));
    }

    [Fact]
    public void NewClassSetting_OpensModalWithTwoBlankClasses()
    {
        var cut = RenderComponent<ClassSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        Assert.Equal("New Class Setting", cut.Find(".wec-modal__header span").TextContent);
        Assert.Equal(2, cut.FindAll(".wec-class-list__row").Count);
    }

    [Fact]
    public void Save_WithoutName_ShowsToastAndKeepsModalOpen()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;

        var cut = RenderComponent<ClassSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();
        cut.Find(".wec-modal__btn--primary").Click();

        Assert.NotNull(toast);
        Assert.True(toast!.IsError);
        Assert.Equal("Setting name is required.", toast.Message);
        Assert.NotEmpty(cut.FindAll(".wec-modal"));
    }

    [Fact]
    public void Save_ValidSetting_AddsRowAndClosesModal()
    {
        var cut = RenderComponent<ClassSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        cut.Find("#cs-name").Change("IMSA");
        const string nameInputs = ".wec-class-list__field:not(.wec-class-list__field--color) input";
        cut.FindAll(nameInputs)[0].Change("GTP");
        cut.FindAll(nameInputs)[1].Change("GTD");
        cut.Find(".wec-modal__btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".wec-modal"));
            Assert.Contains("IMSA", cut.Markup);
        });
    }

    [Fact]
    public void MoveDown_ReordersClassesInDialog()
    {
        var cut = RenderComponent<ClassSettings>();
        cut.WaitForElement("button[title='Edit WEC']").Click();

        cut.FindAll("button[title='Move down']")[0].Click();

        var names = cut.FindAll(".wec-class-list__field:not(.wec-class-list__field--color) input")
            .Select(i => i.GetAttribute("value"));
        Assert.Equal(new[] { "LMP2", "Hypercar", "GT3" }, names);
    }
}
