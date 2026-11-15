namespace MulticlassRace.Services.Tour;

/// <summary>The whole guided tour as read from <c>Tour/tour-steps.json</c>.</summary>
public sealed class TourDefinition
{
    public TourWelcome Welcome { get; set; } = new();

    public List<TourStep> Steps { get; set; } = new();
}

/// <summary>The first-launch question: take the tour, or never ask again.</summary>
public sealed class TourWelcome
{
    public string Title { get; set; } = "Welcome!";

    public string Message { get; set; } = "Would you like a quick tour of the dashboard?";

    public string StartLabel { get; set; } = "Yes, show me around";

    public string DeclineLabel { get; set; } = "No thanks, don't show again";
}

public sealed class TourStep
{
    public string Id { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Page to open before the step is shown; an empty string is the home page.</summary>
    public string? Route { get; set; }

    /// <summary>CSS selector of the element to highlight with a glowing ring. Without one nothing is highlighted.</summary>
    public string? Target { get; set; }

    /// <summary>When true, clicking the highlighted element moves the tour on.</summary>
    public bool PassThrough { get; set; }
}
