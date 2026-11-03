using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace multi_class_race_dashboard.Components.Shared;

/// <summary>
/// Interactive server rendering without the prerender pass. With prerendering, every page ran
/// OnInitializedAsync twice (once on the server for the HTML, once again when the circuit connected),
/// so each query and the page's entrance animation happened twice. The app only ever runs inside its own
/// window, so there is nothing for a prerendered page to help with.
/// </summary>
public static class AppRenderModes
{
    public static readonly IComponentRenderMode Interactive = new InteractiveServerRenderMode(prerender: false);
}
