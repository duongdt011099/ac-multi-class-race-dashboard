namespace MulticlassRace.Services.Database;

public interface IDatabaseTransferService
{
    /// <summary>Writes a copy of the whole database into <paramref name="folder"/> and returns the file's path.</summary>
    Task<string> ExportAsync(string folder);

    /// <summary>
    /// Replaces every row in the current database with the contents of <paramref name="filePath"/>.
    /// A copy of the data being replaced is saved first; its path is returned.
    /// </summary>
    Task<string> ImportAsync(string filePath);
}
