namespace MulticlassRace.Services;

public class UpdateNotificationRequest
{
    public event Action? Requested;

    public void Request()
    {
        Requested?.Invoke();
    }
}
