namespace MulticlassRace.Services;

public class DriverDataChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}