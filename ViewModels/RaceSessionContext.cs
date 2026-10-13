using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public enum RacePopupTab
{
    Teams,
    Practice,
    Qualifying,
    Race
}

/// <summary>
/// What is known about a race when deciding which session tabs can open and whether a session can
/// start. Pure so the rules can be tested without rendering the dialog.
/// </summary>
public sealed record RaceSessionContext(
    bool HasEnteredTeams,
    IReadOnlyCollection<SessionModel>? Sessions,
    int? PracticeSessionMinutes,
    string? TrackName,
    int? TrackPitCount,
    int EnteredCarCount,
    SessionType SelectedSessionType)
{
    public bool HasSessionData => Sessions?.Any(s => s.DriverStandings.Any()) == true;

    public bool HasQualifyingData => HasDataFor(SessionType.Qualifying);

    public bool HasPracticeData => HasDataFor(SessionType.Practice);

    public bool HasSelectedSessionData => HasDataFor(SelectedSessionType);

    public bool CanOpenPractice => HasEnteredTeams && PracticeSessionMinutes != 0;

    public bool CanOpenQualifying => HasEnteredTeams;

    public bool CanOpenRace => HasEnteredTeams && HasQualifyingData;

    public bool HasRaceTrack => !string.IsNullOrWhiteSpace(TrackName);

    public bool ExceedsPitCapacity => HasRaceTrack && TrackPitCount is > 0 && EnteredCarCount > TrackPitCount.Value;

    public bool CanStartSession =>
        HasRaceTrack && !ExceedsPitCapacity && !HasSelectedSessionData &&
        (SelectedSessionType != SessionType.Practice || CanOpenPractice) &&
        (SelectedSessionType != SessionType.Qualifying || CanOpenQualifying) &&
        (SelectedSessionType != SessionType.Race || CanOpenRace);

    /// <summary>Why a session cannot start, or null when it can.</summary>
    public string? StartSessionTitle
    {
        get
        {
            if (!HasRaceTrack)
            {
                return "Select a track for this race before starting a session.";
            }

            if (ExceedsPitCapacity)
            {
                return $"The number of cars ({EnteredCarCount}) is more than the number of pits ({TrackPitCount}). " +
                       "Please change the race track or layout.";
            }

            if (SelectedSessionType == SessionType.Practice && !CanOpenPractice)
            {
                return "Practice is disabled for this race because its duration is set to 0 minutes.";
            }

            if (SelectedSessionType == SessionType.Qualifying && !CanOpenQualifying)
            {
                return "Enter at least one team before starting a session.";
            }

            if (SelectedSessionType == SessionType.Race && !CanOpenRace)
            {
                return "Run a qualifying session and import its results before starting the race.";
            }

            return HasSelectedSessionData
                ? $"A {RaceFormat.SessionTypeLabel(SelectedSessionType).ToLowerInvariant()} session already has data."
                : null;
        }
    }

    /// <summary>The tab (and session type) to show when the dialog first opens.</summary>
    public (RacePopupTab Tab, SessionType Session) InitialTab()
    {
        if (!HasEnteredTeams)
        {
            return (RacePopupTab.Teams, SessionType.Race);
        }

        if (HasQualifyingData)
        {
            return (RacePopupTab.Race, SessionType.Race);
        }

        if (CanOpenPractice && !HasPracticeData)
        {
            return (RacePopupTab.Practice, SessionType.Practice);
        }

        return (RacePopupTab.Qualifying, SessionType.Qualifying);
    }

    private bool HasDataFor(SessionType type)
        => Sessions?.Any(s => s.SessionType == type && s.DriverStandings.Any()) == true;
}
