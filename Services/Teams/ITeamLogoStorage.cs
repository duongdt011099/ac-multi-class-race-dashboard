namespace MulticlassRace.Services.Teams;

public interface ITeamLogoStorage
{
    /// <summary>
    /// Validates and stores a team logo, replacing any earlier logo for the team.
    /// Returns the public URL of the stored file.
    /// </summary>
    /// <exception cref="InvalidLogoException">The file is too large or not an accepted image type.</exception>
    Task<string> SaveAsync(Guid teamId, string originalFileName, string contentType, long length, Func<Stream> openRead);

    /// <summary>
    /// Every distinct logo uploaded so far, newest first. Identical pictures stored under several
    /// teams are listed once.
    /// </summary>
    Task<IReadOnlyList<string>> ListUploadedAsync();

    /// <summary>
    /// Gives the team a copy of an already uploaded logo, so the same picture never has to be uploaded
    /// again. The copy is the team's own file, so replacing it later never touches another team.
    /// Returns the public URL of the team's logo.
    /// </summary>
    /// <exception cref="InvalidLogoException">The logo no longer exists.</exception>
    Task<string> UseUploadedAsync(Guid teamId, string logoUrl);
}

public sealed class InvalidLogoException : Exception
{
    public InvalidLogoException(string message) : base(message)
    {
    }
}
