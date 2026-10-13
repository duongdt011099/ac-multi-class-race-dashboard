using Microsoft.JSInterop;

namespace MulticlassRace.Services.Browser;

/// <summary>
/// Remembers small UI preferences in the browser's local storage. Every call is best effort: with no
/// browser attached (prerendering, a dropped circuit) a preference is simply not remembered.
/// </summary>
public class BrowserStorage
{
    private readonly IJSRuntime _js;

    public BrowserStorage(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await _js.InvokeAsync<string?>("wecLocalStorage.get", key);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            await _js.InvokeVoidAsync("wecLocalStorage.set", key, value);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _js.InvokeVoidAsync("wecLocalStorage.remove", key);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
        }
    }
}
