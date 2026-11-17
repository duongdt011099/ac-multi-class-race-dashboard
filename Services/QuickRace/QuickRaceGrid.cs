using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.QuickRace;

/// <summary>A car of the flattened grid, with the column it sits in.</summary>
public sealed record QuickRaceGridCar(QuickRaceClassModel Class, QuickRaceEntryModel Entry);

/// <summary>
/// The rules of the quick race grid: keeping it consistent, reordering it and turning it into the cars
/// the game starts with. Pure functions over <see cref="QuickRaceSetupModel"/> so they can be tested
/// without a page.
/// </summary>
public static class QuickRaceGrid
{
    /// <summary>Colours handed to new custom classes in turn.</summary>
    public static readonly IReadOnlyList<string> ClassPalette = new[]
    {
        "#e10600", "#1f6feb", "#2ea043", "#f0a500", "#a371f7", "#00b8d9", "#ff7b72", "#8b949e"
    };

    public static QuickRaceClassModel NewCustomClass(IReadOnlyCollection<QuickRaceClassModel> existing)
    {
        var number = existing.Count + 1;
        var names = existing.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (names.Contains($"Class {number}"))
        {
            number++;
        }

        return new QuickRaceClassModel
        {
            Name = $"Class {number}",
            Color = ClassPalette[existing.Count % ClassPalette.Count]
        };
    }

    /// <summary>
    /// The human driver of the quick race: the car chosen in the race settings, or the car of the human
    /// driver shown in the side menu when none was chosen.
    /// </summary>
    public static QuickRaceHuman ResolveHuman(
        QuickRaceSettingsModel settings,
        DriverModel? humanDriver,
        IEnumerable<CarOptionModel> cars)
    {
        var name = string.IsNullOrWhiteSpace(humanDriver?.DriverName) ? "Player" : humanDriver!.DriverName.Trim();

        if (!string.IsNullOrWhiteSpace(settings.HumanCar))
        {
            var car = settings.HumanCar.Trim();
            var display = cars.FirstOrDefault(c => string.Equals(c.CarId, car, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? car;

            return new QuickRaceHuman(name, car, settings.HumanSkin?.Trim() ?? string.Empty, display);
        }

        return new QuickRaceHuman(
            name,
            humanDriver?.Car ?? string.Empty,
            humanDriver?.Skin ?? string.Empty,
            humanDriver?.CarDisplayName ?? humanDriver?.Car ?? string.Empty);
    }

    /// <summary>Every car in start order: column by column, top to bottom.</summary>
    public static IReadOnlyList<QuickRaceGridCar> Flatten(QuickRaceSetupModel setup)
    {
        return setup.ActiveClasses
            .SelectMany(c => c.Entries.Select(e => new QuickRaceGridCar(c, e)))
            .ToList();
    }

    /// <summary>Makes sure a custom grid has a column and that each grid holds the human driver exactly once.</summary>
    public static void Normalize(QuickRaceSetupModel setup, string humanName)
    {
        if (setup.CustomClasses.Count == 0)
        {
            setup.CustomClasses.Add(NewCustomClass(setup.CustomClasses));
        }

        EnsureSingleHuman(setup.CustomClasses, humanName);
        EnsureSingleHuman(setup.SystemClasses, humanName);
    }

    private static void EnsureSingleHuman(List<QuickRaceClassModel> classes, string humanName)
    {
        if (classes.Count == 0)
        {
            return;
        }

        QuickRaceEntryModel? human = null;

        foreach (var column in classes)
        {
            foreach (var entry in column.Entries.Where(e => e.IsHuman).ToList())
            {
                if (human is null)
                {
                    human = entry;
                }
                else
                {
                    column.Entries.Remove(entry);
                }
            }
        }

        if (human is null)
        {
            human = new QuickRaceEntryModel { IsHuman = true };
            classes[0].Entries.Insert(0, human);
        }

        human.DriverName = humanName;
    }

    /// <summary>
    /// Brings the class-system columns in line with the class setting: renamed or recoloured classes
    /// follow it, deleted ones go, new ones are added. The order the user dragged the columns into is kept.
    /// </summary>
    public static void SyncSystemClasses(QuickRaceSetupModel setup, ClassSettingModel? setting)
    {
        if (setting is null)
        {
            setup.SystemClasses.Clear();
            return;
        }

        var items = setting.Items.OrderBy(i => i.Order).ToList();
        var human = setup.SystemClasses.SelectMany(c => c.Entries).FirstOrDefault(e => e.IsHuman);

        var kept = setup.SystemClasses
            .Where(c => items.Any(i => i.ClassSettingItemId == c.ClassId))
            .ToList();

        foreach (var column in kept)
        {
            var item = items.First(i => i.ClassSettingItemId == column.ClassId);
            column.Name = item.Name;
            column.Color = item.Color;
        }

        foreach (var item in items.Where(i => kept.All(c => c.ClassId != i.ClassSettingItemId)))
        {
            kept.Add(new QuickRaceClassModel { ClassId = item.ClassSettingItemId, Name = item.Name, Color = item.Color });
        }

        setup.SystemClasses = kept;

        if (human is not null && kept.Count > 0 && kept.All(c => c.Entries.All(e => !e.IsHuman)))
        {
            kept[0].Entries.Insert(0, human);
        }
    }

    /// <summary>Throws the class-system grid away and starts over from the setting's classes.</summary>
    public static void ResetSystemClasses(QuickRaceSetupModel setup, ClassSettingModel? setting)
    {
        var human = setup.SystemClasses.SelectMany(c => c.Entries).FirstOrDefault(e => e.IsHuman);
        setup.SystemClasses = new List<QuickRaceClassModel>();
        SyncSystemClasses(setup, setting);

        if (human is not null && setup.SystemClasses.Count > 0)
        {
            setup.SystemClasses[0].Entries.Insert(0, human);
        }
    }

    public static QuickRaceClassModel? FindClass(QuickRaceSetupModel setup, Guid classId)
        => setup.ActiveClasses.FirstOrDefault(c => c.ClassId == classId);

    /// <summary>The human driver is part of the grid and cannot be removed; everyone else can.</summary>
    public static bool RemoveEntry(QuickRaceSetupModel setup, Guid entryId)
    {
        foreach (var column in setup.ActiveClasses)
        {
            var entry = column.Entries.FirstOrDefault(e => e.EntryId == entryId);

            if (entry is null)
            {
                continue;
            }

            if (entry.IsHuman)
            {
                return false;
            }

            column.Entries.Remove(entry);
            return true;
        }

        return false;
    }

    public static bool RemoveClass(QuickRaceSetupModel setup, Guid classId)
    {
        if (setup.Mode != QuickRaceGridMode.Custom || setup.CustomClasses.Count <= 1)
        {
            return false;
        }

        var column = setup.CustomClasses.FirstOrDefault(c => c.ClassId == classId);

        if (column is null)
        {
            return false;
        }

        var human = column.Entries.FirstOrDefault(e => e.IsHuman);
        var index = setup.CustomClasses.IndexOf(column);
        setup.CustomClasses.Remove(column);

        if (human is not null)
        {
            setup.CustomClasses[Math.Min(index, setup.CustomClasses.Count - 1)].Entries.Insert(0, human);
        }

        return true;
    }

    /// <summary>
    /// Moves a car to a column, in front of <paramref name="beforeEntryId"/> or to the bottom. In a
    /// class-system grid only the human driver may change class: every other car belongs to the
    /// class its team holds.
    /// </summary>
    public static bool MoveEntry(QuickRaceSetupModel setup, Guid entryId, Guid targetClassId, Guid? beforeEntryId)
    {
        var classes = setup.ActiveClasses;
        var source = classes.FirstOrDefault(c => c.Entries.Any(e => e.EntryId == entryId));
        var target = classes.FirstOrDefault(c => c.ClassId == targetClassId);

        if (source is null || target is null)
        {
            return false;
        }

        var entry = source.Entries.First(e => e.EntryId == entryId);

        if (setup.Mode == QuickRaceGridMode.ExistingClasses && !entry.IsHuman && !ReferenceEquals(source, target))
        {
            return false;
        }

        if (beforeEntryId == entryId)
        {
            return false;
        }

        var sourceIndex = source.Entries.IndexOf(entry);
        var originalTargetIndex = beforeEntryId is { } before ? target.Entries.FindIndex(e => e.EntryId == before) : -1;

        source.Entries.RemoveAt(sourceIndex);

        // The car takes the place of the row it was dropped on. Using that row's original index does
        // that both ways: moving up it lands in front of the row, moving down (the list is one shorter
        // by then) it lands right behind it.
        var insertAt = originalTargetIndex < 0 ? target.Entries.Count : originalTargetIndex;

        target.Entries.Insert(Math.Clamp(insertAt, 0, target.Entries.Count), entry);
        return true;
    }

    /// <summary>Moves a column to where <paramref name="targetClassId"/> is. Only the order changes, never the cars.</summary>
    public static bool MoveClass(QuickRaceSetupModel setup, Guid classId, Guid targetClassId)
    {
        var classes = setup.ActiveClasses;
        var from = classes.FindIndex(c => c.ClassId == classId);
        var to = classes.FindIndex(c => c.ClassId == targetClassId);

        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }

        var column = classes[from];
        classes.RemoveAt(from);
        classes.Insert(to, column);
        return true;
    }

    /// <summary>Identifies which cars are on the grid, whatever their order; a change means qualifying no longer matches.</summary>
    public static string Signature(QuickRaceSetupModel setup)
        => string.Join(",", setup.ActiveClasses.SelectMany(c => c.Entries).Select(e => e.EntryId).Order());

    /// <summary>Forgets the qualifying result: the grid order is the user's own again.</summary>
    public static void ClearQualifying(QuickRaceSetupModel setup)
    {
        setup.QualifyingCompleted = false;
        setup.QualifyingRows = new List<QuickRaceResultRow>();

        foreach (var entry in setup.CustomClasses.Concat(setup.SystemClasses).SelectMany(c => c.Entries))
        {
            entry.QualifyingTimeMs = null;
        }
    }

    /// <summary>
    /// Puts the cars of every column in qualifying order (fastest first; cars without a time keep their order
    /// at the bottom) and marks qualifying as done. The columns keep their order: classes still start in the
    /// order of the class list.
    /// </summary>
    public static void ApplyQualifying(QuickRaceSetupModel setup, IReadOnlyList<QuickRaceResultRow> rows)
    {
        var used = new HashSet<QuickRaceResultRow>();

        QuickRaceResultRow? Find(QuickRaceEntryModel entry)
        {
            var row = entry.IsHuman
                ? rows.FirstOrDefault(r => r.IsHuman)
                : rows.FirstOrDefault(r => !r.IsHuman && !used.Contains(r)
                    && string.Equals(r.DriverName.Trim(), entry.DriverName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Car.Trim(), entry.Car.Trim(), StringComparison.OrdinalIgnoreCase));

            if (row is not null)
            {
                used.Add(row);
            }

            return row;
        }

        foreach (var column in setup.ActiveClasses)
        {
            var ranked = column.Entries
                .Select((entry, index) => (Entry: entry, Row: Find(entry), Index: index))
                .ToList();

            foreach (var item in ranked)
            {
                item.Entry.QualifyingTimeMs = item.Row is { BestLapTimeMs: > 0 } ? item.Row.BestLapTimeMs : null;
            }

            column.Entries = ranked
                .OrderBy(i => i.Row is null ? int.MaxValue : i.Row.Position)
                .ThenBy(i => i.Index)
                .Select(i => i.Entry)
                .ToList();
        }

        setup.QualifyingRows = rows.ToList();
        setup.QualifyingCompleted = true;
    }

    /// <summary>The first thing wrong with the race settings, or null when the race can start.</summary>
    public static string? ValidateSettings(QuickRaceSettingsModel settings, IReadOnlyCollection<TrackLayoutOptionModel> layouts)
    {
        if (string.IsNullOrWhiteSpace(settings.TrackName))
        {
            return "Select a track.";
        }

        if (RaceFormRules.RequiresLayout(layouts) && string.IsNullOrWhiteSpace(settings.TrackLayout))
        {
            return "Select a track layout.";
        }

        if (settings.TimedRaceMinutes is { } timed)
        {
            if (timed < RaceFormRules.MinTimedRaceMinutes || timed > RaceFormRules.MaxTimedRaceMinutes)
            {
                return $"A timed race must last between {RaceFormRules.MinTimedRaceMinutes} minutes and 24 hours.";
            }
        }
        else if (settings.NumberOfLaps is not > 0)
        {
            return "Set the number of laps.";
        }

        return null;
    }

    /// <summary>
    /// An error when more cars are entered than the track has pit boxes (the game cannot place them all
    /// on the grid), or null. A track that reports no pit count is not checked.
    /// </summary>
    public static string? ValidatePitCapacity(int carCount, int? pitCount)
    {
        return pitCount is > 0 && carCount > pitCount.Value
            ? $"The number of cars ({carCount}) is more than the number of pits ({pitCount}). " +
              "Remove drivers or change the race track or layout."
            : null;
    }

    /// <summary>The first thing wrong with the grid, or null when it can start.</summary>
    public static string? ValidateGrid(QuickRaceSetupModel setup, QuickRaceHuman human)
    {
        if (!human.HasCar)
        {
            return "Select a car for the human driver.";
        }

        var cars = Flatten(setup);

        if (cars.Count(c => !c.Entry.IsHuman) == 0)
        {
            return "Add at least one opponent to the grid.";
        }

        if (cars.Any(c => !c.Entry.IsHuman && (string.IsNullOrWhiteSpace(c.Entry.DriverName) || string.IsNullOrWhiteSpace(c.Entry.Car))))
        {
            return "Every driver needs a name and a car.";
        }

        var populated = setup.ActiveClasses.Where(c => c.Entries.Count > 0).ToList();

        if (populated.Any(c => string.IsNullOrWhiteSpace(c.Name)))
        {
            return "Every class needs a name.";
        }

        if (populated.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            return "Two classes share the same name.";
        }

        return null;
    }

    /// <summary>
    /// The cars as the game gets them: the human is grid index 0, the others follow in start order.
    /// <paramref name="playerStartPosition"/> is the human's 1-based place on the grid.
    /// </summary>
    public static (IReadOnlyList<QuickRaceLaunchCar> Cars, int PlayerStartPosition) BuildLaunchCars(
        QuickRaceSetupModel setup,
        QuickRaceHuman human)
    {
        var flat = Flatten(setup);
        var cars = new List<QuickRaceLaunchCar>();
        var humanCar = flat.FirstOrDefault(c => c.Entry.IsHuman);

        cars.Add(new QuickRaceLaunchCar
        {
            Index = 0,
            DriverName = human.DriverName,
            Car = human.Car,
            Skin = human.Skin,
            CarDisplayName = human.CarDisplayName,
            ClassName = humanCar?.Class.Name.Trim() ?? string.Empty,
            ClassColor = humanCar?.Class.Color ?? RaceFormat.UnknownClassColor,
            IsHuman = true
        });

        foreach (var item in flat.Where(c => !c.Entry.IsHuman))
        {
            cars.Add(new QuickRaceLaunchCar
            {
                Index = cars.Count,
                DriverName = item.Entry.DriverName.Trim(),
                Car = item.Entry.Car.Trim(),
                Skin = item.Entry.Skin.Trim(),
                CarDisplayName = item.Entry.CarDisplayName,
                TeamName = item.Entry.TeamName,
                ClassName = item.Class.Name.Trim(),
                ClassColor = item.Class.Color
            });
        }

        var position = humanCar is null ? 1 : flat.TakeWhile(c => !c.Entry.IsHuman).Count() + 1;

        return (cars, position);
    }
}
