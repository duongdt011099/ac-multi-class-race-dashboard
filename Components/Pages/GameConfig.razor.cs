using Microsoft.AspNetCore.Components;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Results;

namespace multi_class_race_dashboard.Components.Pages;

public partial class GameConfig
{
    [Inject] private IAssettoCorsaGameConfigService ConfigService { get; set; } = default!;

    [Inject] private AssettoCorsaPathResolver PathResolver { get; set; } = default!;

    [Inject] private GameConfigChangeNotifier ConfigNotifier { get; set; } = default!;

    private AssettoCorsaGameConfigModel config = new();
    private bool isSaving;
    private string? gameFolder;
    private AssettoCorsaPathSource source;

    private static string DefaultLuaResultPath => LuaResultPaths.DefaultRoot;

    private string GameFolderSourceText => source switch
    {
        AssettoCorsaPathSource.Installer => "The installer (install.json)",
        AssettoCorsaPathSource.Saved => "A previously saved configuration",
        AssettoCorsaPathSource.AutoDetected => "Automatic detection",
        _ => "Not found. Reinstall the dashboard and point it at the game folder."
    };

    protected override async Task OnInitializedAsync()
    {
        var existing = await ConfigService.GetAsync();

        if (existing is not null)
        {
            config = existing;
        }

        await ReadLocationAsync();
    }

    private async Task ReadLocationAsync()
    {
        var location = await PathResolver.GetAsync();
        gameFolder = location.Path;
        source = location.Source;
    }

    private Task SetLuaResultPath(string? value)
    {
        config.LuaResultPath = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (isSaving)
        {
            return;
        }

        isSaving = true;

        try
        {
            var created = await ConfigService.SaveAsync(config);
            ConfigNotifier.NotifyChanged();
            Toast.Show(created ? "Configuration created." : "Configuration saved.");
        }
        finally
        {
            isSaving = false;
        }
    }
}
