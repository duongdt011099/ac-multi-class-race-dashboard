using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public class AppUpdatePreferenceService : IAppUpdatePreferenceService
{
    private readonly IAppUpdateStateRepository _stateRepository;

    public AppUpdatePreferenceService(IAppUpdateStateRepository stateRepository)
    {
        _stateRepository = stateRepository;
    }

    public async Task<string> GetLastSeenVersionAsync()
    {
        var state = await _stateRepository.GetAsync();
        return state?.LastSeenVersion ?? string.Empty;
    }

    public async Task<DateTimeOffset?> GetLastCheckedAtAsync()
    {
        var state = await _stateRepository.GetAsync();
        return state?.LastCheckedAtUtc;
    }

    public async Task MarkSeenAsync(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var state = await _stateRepository.GetAsync();

        if (state is null)
        {
            await _stateRepository.AddAsync(new AppUpdateState
            {
                AppUpdateStateId = Guid.NewGuid(),
                LastSeenVersion = version,
                LastCheckedAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        state.LastSeenVersion = version.Trim();
        state.LastCheckedAtUtc = DateTimeOffset.UtcNow;

        await _stateRepository.UpdateAsync(state);
    }

    public async Task MarkCheckedAsync()
    {
        var state = await _stateRepository.GetAsync();

        if (state is null)
        {
            await _stateRepository.AddAsync(new AppUpdateState
            {
                AppUpdateStateId = Guid.NewGuid(),
                LastCheckedAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        state.LastCheckedAtUtc = DateTimeOffset.UtcNow;

        await _stateRepository.UpdateAsync(state);
    }
}
