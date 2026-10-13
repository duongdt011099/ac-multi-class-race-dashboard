namespace MulticlassRace.Services.Teams;

public interface ITeamLogoStorage
{
    /// <summary>
    /// Validates and stores a team logo, replacing any earlier logo for the team.
    /// Returns the public URL of the stored file.
    /// </summary>
    /// <exception cref="InvalidLogoException">The file is too large or not an accepted image type.</exception>
    Task<string> SaveAsync(Guid teamId, string originalFileName, string contentType, long length, Func<Stream> openRead);
}

public sealed class InvalidLogoException : Exception
{
    public InvalidLogoException(string message) : base(message)
    {
    }
}
