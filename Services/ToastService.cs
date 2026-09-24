namespace MulticlassRace.Services;

public sealed record ToastMessage(string Message, bool IsError);

public class ToastService
{
    public event Action<ToastMessage>? ToastChanged;

    public void Show(string message, bool isError = false)
    {
        ToastChanged?.Invoke(new ToastMessage(message, isError));
    }
}