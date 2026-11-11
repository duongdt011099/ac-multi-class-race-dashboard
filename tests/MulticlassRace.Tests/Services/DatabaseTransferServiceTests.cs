using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Services.Database;

namespace MulticlassRace.Tests.Services;

public class DatabaseTransferServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcr-transfer-" + Guid.NewGuid().ToString("N"));
    private readonly string _livePath;
    private readonly string _exportFolder;
    private readonly string _safetyFolder;

    public DatabaseTransferServiceTests()
    {
        Directory.CreateDirectory(_root);
        _livePath = Path.Combine(_root, "live.db");
        _exportFolder = Path.Combine(_root, "export");
        _safetyFolder = Path.Combine(_root, "safety");
        Directory.CreateDirectory(_exportFolder);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_livePath}")
            .Options;
        var context = new AppDbContext(options);
        context.Database.Migrate();

        return context;
    }

    private static async Task AddTeamAsync(AppDbContext context, string name)
    {
        context.Teams.Add(new Team { TeamId = Guid.NewGuid(), TeamName = name, IsActive = true });
        await context.SaveChangesAsync();
    }

    private static async Task<List<string>> TeamNamesAsync(AppDbContext context) =>
        await context.Teams.AsNoTracking().OrderBy(t => t.TeamName).Select(t => t.TeamName).ToListAsync();

    [Fact]
    public async Task Import_ReplacesAllCurrentDataAndKeepsASafetyCopy()
    {
        await using var context = CreateContext();
        var service = new DatabaseTransferService(context, _safetyFolder);

        await AddTeamAsync(context, "Original");
        var exported = await service.ExportAsync(_exportFolder);
        await AddTeamAsync(context, "Added later");

        var safetyCopy = await service.ImportAsync(exported);

        Assert.Equal(new[] { "Original" }, await TeamNamesAsync(context));
        Assert.True(File.Exists(safetyCopy));
        Assert.StartsWith(_safetyFolder, safetyCopy);

        // The safety copy holds what was replaced, so the import can be undone by importing it.
        await service.ImportAsync(safetyCopy);
        Assert.Equal(new[] { "Added later", "Original" }, await TeamNamesAsync(context));
    }

    [Fact]
    public async Task Export_WritesASingleSelfContainedFile()
    {
        await using var context = CreateContext();
        var service = new DatabaseTransferService(context, _safetyFolder);

        var exported = await service.ExportAsync(_exportFolder);

        Assert.True(File.Exists(exported));
        Assert.Single(Directory.GetFiles(_exportFolder));
    }

    [Fact]
    public async Task Import_RejectsAFileThatIsNotADatabase()
    {
        await using var context = CreateContext();
        var service = new DatabaseTransferService(context, _safetyFolder);
        await AddTeamAsync(context, "Original");

        var bogus = Path.Combine(_root, "notes.db");
        await File.WriteAllTextAsync(bogus, "this is not a database, just some text that is long enough");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(bogus));

        Assert.Equal(new[] { "Original" }, await TeamNamesAsync(context));
        Assert.False(Directory.Exists(_safetyFolder));
    }

    [Fact]
    public async Task Import_RejectsADatabaseFromAnotherApp()
    {
        await using var context = CreateContext();
        var service = new DatabaseTransferService(context, _safetyFolder);

        var foreign = Path.Combine(_root, "foreign.db");
        await using (var connection = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Things (Id INTEGER);";
            await command.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(foreign));

        Assert.Contains("not a database exported from this app", ex.Message);
    }

    [Fact]
    public async Task Export_RejectsAMissingFolder()
    {
        await using var context = CreateContext();
        var service = new DatabaseTransferService(context, _safetyFolder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(Path.Combine(_root, "missing")));
    }
}
