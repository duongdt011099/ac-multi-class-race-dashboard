using Bunit;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Tests.Support;
using MulticlassRace.Services.Notifications;

namespace MulticlassRace.Tests.Pages;

public class PointSettingsPageTests : PageTestContext
{
    [Fact]
    public void Renders_SeededStandardSettingWithPreview()
    {
        var cut = RenderComponent<PointSettings>();

        cut.WaitForAssertion(() => Assert.Contains("Standard", cut.Markup));
        Assert.StartsWith("1:25 › 2:18 › 3:15", cut.Find(".wec-points-preview").TextContent);
        Assert.NotEmpty(cut.FindAll(".wec-badge--default"));
    }

    [Fact]
    public void Add_OpensModalWithTwoPositions()
    {
        var cut = RenderComponent<PointSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        Assert.Equal("New Point Setting", cut.Find(".wec-modal__header span").TextContent);
        Assert.Equal(new[] { "1", "2" }, cut.FindAll(".wec-setting-entry-position").Select(p => p.TextContent));
    }

    [Fact]
    public void AddEntry_AppendsNextPosition()
    {
        var cut = RenderComponent<PointSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        cut.Find(".wec-setting-entries .wec-toolbar__btn").Click();

        Assert.Equal(new[] { "1", "2", "3" }, cut.FindAll(".wec-setting-entry-position").Select(p => p.TextContent));
    }

    [Fact]
    public void NewSetting_WhenDefaultExists_CannotToggleDefault()
    {
        var cut = RenderComponent<PointSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        Assert.True(cut.Find("#setting-default").HasAttribute("disabled"));
        Assert.Contains("Standard", cut.Find(".wec-form-hint").TextContent);
    }

    [Fact]
    public void Save_InvalidPoints_ShowsServiceMessageAsToast()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;

        var cut = RenderComponent<PointSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();
        cut.Find("#setting-name").Change("Broken");
        cut.Find(".wec-modal__btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(toast);
            Assert.True(toast!.IsError);
        });
    }

    [Fact]
    public void Delete_ShowsConfirmThenRemovesSetting()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;

        var cut = RenderComponent<PointSettings>();
        cut.WaitForElement(".wec-toolbar__btn").Click();
        cut.Find("#setting-name").Change("Extra");
        cut.FindAll(".wec-setting-entry-input")[0].Change("10");
        cut.FindAll(".wec-setting-entry-input")[1].Change("5");
        cut.Find(".wec-modal__btn--primary").Click();
        cut.WaitForAssertion(() => Assert.True(cut.Markup.Contains("Extra"), toast?.Message ?? "no toast"));

        cut.Find("button[title='Delete Extra']").Click();
        Assert.Contains("Delete Extra?", cut.Find(".wec-modal__body").TextContent);
        cut.Find(".wec-modal__btn--danger").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Extra", cut.Find("tbody").TextContent));
    }
}
