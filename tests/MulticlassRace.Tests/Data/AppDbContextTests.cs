using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Tests.Support;

namespace MulticlassRace.Tests.Data;

public class AppDbContextTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Seed_ContainsDefaultWecClassSettingWithThreeItems()
    {
        using var context = _db.CreateContext();

        var setting = await context.ClassSettings.Include(c => c.Items).SingleAsync();

        Assert.Equal("WEC", setting.ClassSettingName);
        Assert.True(setting.IsDefault);
        Assert.Equal(new[] { "Hypercar", "LMP2", "GT3" }, setting.Items.OrderBy(i => i.Order).Select(i => i.Name));
    }

    [Fact]
    public async Task Seed_ContainsStandardPointSetting()
    {
        using var context = _db.CreateContext();

        var setting = await context.PointSettings.SingleAsync();

        Assert.Equal("Standard", setting.SettingName);
        Assert.Equal(10, setting.Config.Count);
    }

    [Fact]
    public async Task PointSettingConfig_RoundTripsThroughJson()
    {
        using (var context = _db.CreateContext())
        {
            context.PointSettings.Add(new PointSetting
            {
                SettingId = Guid.NewGuid(),
                SettingName = "RT",
                IsDefault = false,
                Config = new() { ["1"] = 7, ["2"] = 3 }
            });
            await context.SaveChangesAsync();
        }

        using var reader = _db.CreateContext();
        var loaded = await reader.PointSettings.SingleAsync(p => p.SettingName == "RT");

        Assert.Equal(7, loaded.Config["1"]);
        Assert.Equal(3, loaded.Config["2"]);
    }

    [Fact]
    public async Task ImportedLuaResult_FullPathIsUnique()
    {
        using var context = _db.CreateContext();
        var index = context.Model.FindEntityType(typeof(ImportedLuaResult))!
            .GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ImportedLuaResult.FullPath));

        Assert.True(index.IsUnique);
        await Task.CompletedTask;
    }

    [Fact]
    public void Model_HasNoPendingChangesComparedToMigrations()
    {
        using var context = _db.CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("Data Source=app.db", @"C:\root", true)]
    [InlineData("Data Source=C:\\abs\\app.db", @"C:\root", false)]
    public void ResolveForDesignTime_MakesRelativePathsAbsolute(string input, string root, bool changed)
    {
        var result = MulticlassRace.Data.DependencyInjection.ResolveForDesignTime(input, root);

        Assert.Equal(changed, result != input);
        if (changed)
        {
            Assert.Contains(@"C:\root\app.db", result);
        }
    }
}
