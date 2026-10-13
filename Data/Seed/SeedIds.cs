namespace MulticlassRace.Data.Seed;

/// <summary>
/// Ids of the rows every database starts with. They are fixed because migrations and seed data refer
/// to them, so they must never change once a database has been created from them.
/// </summary>
public static class SeedIds
{
    public const string DefaultClassSettingId = "2254e881-f300-4154-a84f-600196de6000";

    public const string DefaultHypercarItemId = "2254e881-f300-4154-a84f-600196de6081";

    public const string DefaultLmp2ItemId = "2254e881-f300-4154-a84f-600196de6082";

    public const string DefaultGt3ItemId = "2254e881-f300-4154-a84f-600196de6083";

    public const string DefaultPointSettingId = "d8f10b2a-5a6b-4c7d-8e9f-0a1b2c3d4e5f";
}
