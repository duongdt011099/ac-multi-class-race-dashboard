namespace MulticlassRace.Services;

public class GameConfigChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}