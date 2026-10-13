namespace MulticlassRace.Services.Drivers;

public class DriverDataChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}