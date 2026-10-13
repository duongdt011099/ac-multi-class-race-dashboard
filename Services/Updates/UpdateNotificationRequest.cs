namespace MulticlassRace.Services.Updates;

public class UpdateNotificationRequest
{
    public event Action? Requested;

    public void Request()
    {
        Requested?.Invoke();
    }
}
