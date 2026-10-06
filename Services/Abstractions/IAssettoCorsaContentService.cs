using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IAssettoCorsaContentService
{
    Task<IReadOnlyList<string>> GetCarsAsync();

    Task<IReadOnlyList<CarOptionModel>> GetCarOptionsAsync();

    Task<string?> GetCarDisplayNameAsync(string car);

    Task<IReadOnlyList<string>> GetSkinsAsync(string car);

    string? BuildSkinPreviewUrl(string? car, string? skin);
}
