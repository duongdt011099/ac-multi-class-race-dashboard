using Bunit;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.Drivers;

namespace MulticlassRace.Tests.Pages;

public class DriversPageTests : PageTestContext
{
    private static ClassTagModel Gt3 => new() { ItemName = "GT3", SettingName = "WEC", Color = "#3fa34d" };

    [Fact]
    public void Renders_EmptyStateWhenNoDrivers()
    {
        var cut = RenderComponent<Drivers>();

        Assert.Equal("No drivers found", cut.WaitForElement("td.wec-empty").TextContent);
    }

    [Fact]
    public void Renders_DriverRowWithClassBadgeAndDashesForMissingAi()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Alonso", tags: Gt3));

        var cut = RenderComponent<Drivers>();

        cut.WaitForElement("tbody tr");
        Assert.Equal("GT3", cut.Find(".wec-badge").TextContent);
        Assert.Contains("Alonso", cut.Find("tbody").TextContent);
        Assert.Equal(new[] { "—", "—" }, cut.FindAll("tbody td").Skip(7).Take(2).Select(td => td.TextContent));
    }

    [Fact]
    public void ClassFilter_OffersUncategorizedOnlyWhenSomeDriverHasNoClass()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Tagged", tags: Gt3));
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("tbody tr");

        Assert.DoesNotContain(cut.FindAll("#filter-driver-class option"), o => o.TextContent == "Uncategorized");

        DriverService.Drivers.Add(FakeDriverService.Driver("Plain"));
        var withPlain = RenderComponent<Drivers>();
        withPlain.WaitForElement("tbody tr");

        Assert.Contains(withPlain.FindAll("#filter-driver-class option"), o => o.TextContent == "Uncategorized");
    }

    [Fact]
    public void ClassFilter_UncategorizedShowsOnlyClasslessDrivers()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Tagged", tags: Gt3));
        DriverService.Drivers.Add(FakeDriverService.Driver("Plain"));
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("tbody tr");

        cut.Find("#filter-driver-class").Change(DriverListQuery.UncategorizedKey);

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Plain", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void Search_MatchesDriverName()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Alonso"));
        DriverService.Drivers.Add(FakeDriverService.Driver("Button"));
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("tbody tr");

        cut.Find("#filter-driver-search").Input("butt");

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Button", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void SortByDriver_TogglesOrder()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Alonso"));
        DriverService.Drivers.Add(FakeDriverService.Driver("Button"));
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("tbody tr");

        IEnumerable<string> Names() => cut.FindAll("tbody tr td:nth-child(4)").Select(td => td.TextContent);
        Assert.Equal(new[] { "Alonso", "Button" }, Names());

        cut.Find("th .wec-table__sort").Click();

        Assert.Equal(new[] { "Button", "Alonso" }, Names());
    }

    [Fact]
    public void NewDriver_RequiresNameThenCar()
    {
        var toasts = new List<string>();
        Toast.ToastChanged += message => toasts.Add(message.Message);
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        cut.Find(".wec-modal__btn--primary").Click();
        cut.Find("#driver-name").Change("Alonso");
        cut.Find(".wec-modal__btn--primary").Click();

        Assert.Equal(new[] { "Driver name is required.", "Car is required." }, toasts);
    }

    [Fact]
    public void NewDriver_SelectingCarLoadsSkinsAndEnablesSkinSelect()
    {
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        Assert.True(cut.Find("#driver-skin").HasAttribute("disabled"));
        Assert.Equal("Select a car first...", cut.Find("#driver-skin option").TextContent);

        cut.Find("#driver-car").Change("ferrari_499p");

        cut.WaitForAssertion(() => Assert.False(cut.Find("#driver-skin").HasAttribute("disabled")));
        Assert.Equal(new[] { "Select skin...", "red", "yellow" }, cut.FindAll("#driver-skin option").Select(o => o.TextContent));
    }

    [Fact]
    public void NewDriver_SavesAndAppearsInTable()
    {
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        cut.Find("#driver-name").Change("Alonso");
        cut.Find("#driver-car").Change("ferrari_499p");
        cut.Find(".wec-modal__btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".wec-modal"));
            Assert.Contains("Alonso", cut.Find("tbody").TextContent);
        });
        Assert.Equal("ferrari_499p", Assert.Single(DriverService.Saved).Car);
    }

    [Fact]
    public void EditDriver_ShowsDeleteButtonAndMissingCarWarning()
    {
        var driver = FakeDriverService.Driver("Alonso");
        driver.Car = "removed_car";
        DriverService.Drivers.Add(driver);
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("button[title='Edit Alonso']").Click();

        Assert.Single(cut.FindAll(".wec-modal__footer .wec-modal__btn--danger"));
        Assert.Contains(cut.FindAll("#driver-car option"), o => o.TextContent.Contains("(not found on disk)"));
    }

    [Fact]
    public void EditDriver_DeleteGoesThroughConfirmation()
    {
        var driver = FakeDriverService.Driver("Alonso");
        DriverService.Drivers.Add(driver);
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement("button[title='Edit Alonso']").Click();

        cut.Find(".wec-modal__footer .wec-modal__btn--danger").Click();
        Assert.Equal("Delete Driver", cut.Find(".wec-modal__header span").TextContent);

        cut.Find(".wec-modal__btn--danger").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("td.wec-empty")));
        Assert.Equal(new[] { driver.DriverId }, DriverService.Deleted);
    }

    [Fact]
    public void HumanCheckbox_DisabledWhenAnotherDriverIsHuman()
    {
        var human = FakeDriverService.Driver("Human");
        human.IsHuman = true;
        DriverService.Drivers.Add(human);
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        Assert.True(cut.Find("#driver-human").HasAttribute("disabled"));
        Assert.Contains("Human", cut.Find(".wec-form-field--checkbox-human").GetAttribute("title"));
    }

    [Fact]
    public void CarImage_OpensViewerAndBackdropCloses()
    {
        var driver = FakeDriverService.Driver("Alonso");
        driver.CarImageUrl = "/car.png";
        DriverService.Drivers.Add(driver);
        var cut = RenderComponent<Drivers>();
        cut.WaitForElement(".wec-drivers__car-btn").Click();

        cut.WaitForAssertion(() => Assert.Equal("Car", cut.Find(".wec-modal__header span").TextContent));
        Assert.Equal("/car.png", cut.Find(".wec-image-viewer__img").GetAttribute("src"));

        cut.Find(".wec-modal-overlay").Click();

        Assert.Empty(cut.FindAll(".wec-modal"));
    }
}
