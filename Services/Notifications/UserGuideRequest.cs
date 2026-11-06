namespace MulticlassRace.Services.Notifications;

/// <summary>Lets the sidebar ask the layout to open the how-to-use guide.</summary>
public class UserGuideRequest
{
    public event Action? Requested;

    public void Request() => Requested?.Invoke();
}
