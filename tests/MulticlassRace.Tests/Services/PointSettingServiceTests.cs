using Microsoft.EntityFrameworkCore;
using MulticlassRace.Repositories;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.Settings;

namespace MulticlassRace.Tests.Services;

public class PointSettingServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private PointSettingService CreateService(out MulticlassRace.Data.AppDbContext context)
    {
        context = _db.CreateContext();
        return new PointSettingService(new PointSettingRepository(context));
    }

    private static PointSettingFormModel Form(string name, bool isDefault, params (string pos, string pts)[] rows)
    {
        return new PointSettingFormModel
        {
            SettingName = name,
            IsDefault = isDefault,
            Entries = rows.Select(r => new PointSettingEntryModel { Position = r.pos, Points = r.pts }).ToList()
        };
    }

    [Fact]
    public async Task GetAll_ReturnsSeededDefaultFirst()
    {
        var service = CreateService(out _);

        var all = (await service.GetAllAsync()).ToList();

        Assert.Single(all);
        Assert.True(all[0].IsDefault);
        Assert.Equal(25, all[0].Config["1"]);
    }

    [Fact]
    public async Task Create_TrimsNameAndSortsPositions()
    {
        var service = CreateService(out var context);

        await service.CreateAsync(Form("  Custom  ", false, ("2", "6"), ("1", "10")));

        var created = await context.PointSettings.SingleAsync(p => p.SettingName == "Custom");
        Assert.Equal(new[] { "1", "2" }, created.Config.Keys.ToArray());
        Assert.Equal(10, created.Config["1"]);
    }

    [Fact]
    public async Task Create_AsDefault_ClearsPreviousDefault()
    {
        var service = CreateService(out _);

        await service.CreateAsync(Form("New default", true, ("1", "5")));

        var all = (await service.GetAllAsync()).ToList();
        Assert.Single(all, s => s.IsDefault);
        Assert.Equal("New default", all.Single(s => s.IsDefault).SettingName);
    }

    [Fact]
    public async Task Create_SkipsBlankRows()
    {
        var service = CreateService(out var context);

        await service.CreateAsync(Form("Blanks", false, ("1", "9"), ("", "")));

        var created = await context.PointSettings.SingleAsync(p => p.SettingName == "Blanks");
        Assert.Single(created.Config);
    }

    [Theory]
    [InlineData("0", "5")]
    [InlineData("abc", "5")]
    [InlineData("1", "abc")]
    public async Task Create_RejectsInvalidRows(string position, string points)
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Form("Bad", false, (position, points))));
    }

    [Fact]
    public async Task Create_RejectsDuplicatePositions()
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Form("Dup", false, ("1", "5"), ("1", "6"))));
    }

    [Fact]
    public async Task Create_RejectsEmptyConfig()
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Form("Empty", false)));
    }

    [Fact]
    public async Task Update_UnknownId_Throws()
    {
        var service = CreateService(out _);
        var form = Form("X", false, ("1", "1"));
        form.SettingId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(form));
    }

    [Fact]
    public async Task Update_ChangesValues()
    {
        var service = CreateService(out _);
        var existing = (await service.GetAllAsync()).Single();

        await service.UpdateAsync(new PointSettingFormModel
        {
            SettingId = existing.SettingId,
            SettingName = "Renamed",
            IsDefault = true,
            Entries = new() { new() { Position = "1", Points = "50" } }
        });

        var updated = (await service.GetAllAsync()).Single();
        Assert.Equal("Renamed", updated.SettingName);
        Assert.Equal(50, updated.Config["1"]);
    }

    [Fact]
    public async Task Delete_RemovesUnusedSetting()
    {
        var service = CreateService(out _);
        await service.CreateAsync(Form("Temp", false, ("1", "1")));
        var temp = (await service.GetAllAsync()).Single(s => s.SettingName == "Temp");

        await service.DeleteAsync(temp.SettingId);

        Assert.DoesNotContain(await service.GetAllAsync(), s => s.SettingName == "Temp");
    }
}
