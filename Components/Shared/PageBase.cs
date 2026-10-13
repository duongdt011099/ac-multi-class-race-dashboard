using Microsoft.AspNetCore.Components;
using MulticlassRace.Services;

namespace multi_class_race_dashboard.Components.Shared;

/// <summary>Common base for pages: toast access and the "run, toast on failure" pattern every page repeats.</summary>
public abstract class PageBase : ComponentBase
{
    [Inject] protected ToastService Toast { get; set; } = default!;

    /// <summary>
    /// Runs <paramref name="action"/>; on failure shows an error toast and returns false.
    /// With no prefix the toast is just the exception message, which is what service validation errors are written for.
    /// </summary>
    protected async Task<bool> TryAsync(Func<Task> action, string? errorPrefix = null)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Toast.Show(errorPrefix is null ? ex.Message : $"{errorPrefix}: {ex.Message}", isError: true);
            return false;
        }
    }

    /// <summary>Shows a validation error toast and returns false when <paramref name="value"/> is blank.</summary>
    protected bool RequireText(string? value, string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        Toast.Show(message, isError: true);
        return false;
    }
}
