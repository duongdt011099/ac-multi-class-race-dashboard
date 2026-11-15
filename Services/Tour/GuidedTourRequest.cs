namespace MulticlassRace.Services.Tour;

/// <summary>Lets the sidebar ask the layout to start the guided tour.</summary>
public class GuidedTourRequest
{
    public event Action? Requested;

    public void Request() => Requested?.Invoke();
}
