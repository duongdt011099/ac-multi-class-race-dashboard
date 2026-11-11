namespace MulticlassRace.Services.Notifications;

/// <summary>Lets the side menu ask the layout to open the safety car panel (which must sit outside the sidebar).</summary>
public class SafetyCarPanelRequest
{
    public event Action? Requested;

    public void Request() => Requested?.Invoke();
}
