namespace MulticlassRace.Services.Abstractions;

public interface IAssettoCorsaContentService
{
    Task<IReadOnlyList<string>> GetCarsAsync();

    Task<IReadOnlyList<string>> GetSkinsAsync(string car);

    string? BuildSkinPreviewUrl(string? car, string? skin);
}
