using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;

namespace MulticlassRace.Tests.Support;

/// <summary>In-memory SQLite database built from the real model, so the app's app.db is never touched.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
