using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;

namespace MulticlassRace.Services.Database;

public class DatabaseTransferService : IDatabaseTransferService
{
    private readonly AppDbContext _context;
    private readonly string _safetyCopyFolder;

    public DatabaseTransferService(AppDbContext context)
        : this(context, Path.Combine(AppContext.BaseDirectory, "backups"))
    {
    }

    public DatabaseTransferService(AppDbContext context, string safetyCopyFolder)
    {
        _context = context;
        _safetyCopyFolder = safetyCopyFolder;
    }

    public async Task<string> ExportAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new InvalidOperationException("Choose an existing folder to export to.");
        }

        var path = Path.Combine(folder, $"multiclass-race-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        await Task.Run(() => CopyLiveDatabaseTo(path));

        return path;
    }

    public async Task<string> ImportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new InvalidOperationException("The selected database file does not exist.");
        }

        ValidateImportFile(filePath);

        Directory.CreateDirectory(_safetyCopyFolder);
        var safetyCopy = Path.Combine(_safetyCopyFolder, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        await Task.Run(() =>
        {
            CopyLiveDatabaseTo(safetyCopy);

            try
            {
                using var source = new SqliteConnection(ReadOnlyConnectionString(filePath));
                source.Open();

                using var live = new SqliteConnection(LiveConnectionString);
                live.Open();

                // The backup API overwrites the open database page by page, so the app's own file handle,
                // its journal and any reader stay consistent; nothing has to be closed or swapped on disk.
                source.BackupDatabase(live);
            }
            catch (SqliteException ex)
            {
                throw new InvalidOperationException(
                    $"The database could not be replaced ({ex.Message}). Your data was left as it was.", ex);
            }
        });

        // A file exported by an older version is brought up to the current schema.
        _context.ChangeTracker.Clear();
        await _context.Database.MigrateAsync();

        return safetyCopy;
    }

    private string LiveConnectionString =>
        _context.Database.GetConnectionString()
        ?? throw new InvalidOperationException("The database connection string is not available.");

    private static string ReadOnlyConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();

    private void CopyLiveDatabaseTo(string path)
    {
        using var live = new SqliteConnection(LiveConnectionString);
        live.Open();

        var destinationString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

        using (var destination = new SqliteConnection(destinationString))
        {
            destination.Open();
            live.BackupDatabase(destination);

            // The live database uses a write-ahead log; the copy should be one self-contained file.
            using var command = destination.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE;";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Rejects anything that is not an intact database written by this app (or an older version of it).</summary>
    private void ValidateImportFile(string filePath)
    {
        try
        {
            using var connection = new SqliteConnection(ReadOnlyConnectionString(filePath));
            connection.Open();

            if (Scalar(connection, "PRAGMA integrity_check;") is not "ok")
            {
                throw new InvalidOperationException("The selected file is damaged and cannot be imported.");
            }

            var hasHistory = Convert.ToInt32(Scalar(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';")) > 0;

            if (!hasHistory)
            {
                throw new InvalidOperationException("The selected file is not a database exported from this app.");
            }

            var known = _context.Database.GetMigrations().ToHashSet();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (!known.Contains(reader.GetString(0)))
                {
                    throw new InvalidOperationException(
                        "The selected database was exported from a newer version of the app. Update the app first.");
                }
            }
        }
        catch (SqliteException)
        {
            throw new InvalidOperationException("The selected file is not a SQLite database.");
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }
}
