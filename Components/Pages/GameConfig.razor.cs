using Microsoft.AspNetCore.Components;
using MulticlassRace.Services.AssettoCorsa;

namespace multi_class_race_dashboard.Components.Pages;

/// <summary>
/// Read-only: shows where the game was found. It is no longer in the menu; it is only reached when the
/// game folder is missing, which is when the gate redirects here.
/// </summary>
public partial class GameConfig
{
    [Inject] private AssettoCorsaPathResolver PathResolver { get; set; } = default!;

    private string? gameFolder;
    private AssettoCorsaPathSource source;

    private string GameFolderSourceText => source switch
    {
        AssettoCorsaPathSource.Installer => "The installer (install.json)",
        AssettoCorsaPathSource.Saved => "A previously saved configuration",
        AssettoCorsaPathSource.AutoDetected => "Automatic detection",
        _ => "Not found. Reinstall the dashboard and point it at the game folder."
    };

    protected override async Task OnInitializedAsync()
    {
        await ReadLocationAsync();
    }

    private async Task ReadLocationAsync()
    {
        var location = await PathResolver.GetAsync();
        gameFolder = location.Path;
        source = location.Source;
    }
}
