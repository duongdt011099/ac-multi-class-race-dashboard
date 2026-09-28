namespace MulticlassRace.Services.ImportModels;

internal sealed class CmSessionFile
{
    public string Track { get; set; } = string.Empty;

    public int Number_Of_Sessions { get; set; }

    public CmPlayer[] Players { get; set; } = Array.Empty<CmPlayer>();

    public CmSession[] Sessions { get; set; } = Array.Empty<CmSession>();
}
