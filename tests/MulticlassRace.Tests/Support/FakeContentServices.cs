using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Support;

public sealed class FakeContentService : IAssettoCorsaContentService
{
    public List<CarOptionModel> Cars { get; } = new()
    {
        new CarOptionModel("ferrari_499p", "Ferrari 499P"),
        new CarOptionModel("porsche_963", "Porsche 963")
    };

    public Dictionary<string, List<string>> Skins { get; } = new()
    {
        ["ferrari_499p"] = new() { "red", "yellow" },
        ["porsche_963"] = new() { "penske" }
    };

    public Task<IReadOnlyList<string>> GetCarsAsync() => Task.FromResult<IReadOnlyList<string>>(Cars.Select(c => c.CarId).ToList());

    public Task<IReadOnlyList<CarOptionModel>> GetCarOptionsAsync() => Task.FromResult<IReadOnlyList<CarOptionModel>>(Cars.ToList());

    public Task<string?> GetCarDisplayNameAsync(string car)
        => Task.FromResult(Cars.FirstOrDefault(c => c.CarId == car)?.DisplayName);

    public Task<IReadOnlyList<string>> GetSkinsAsync(string car)
        => Task.FromResult<IReadOnlyList<string>>(Skins.TryGetValue(car, out var skins) ? skins : new List<string>());

    public string? BuildSkinPreviewUrl(string? car, string? skin)
        => string.IsNullOrWhiteSpace(car) || string.IsNullOrWhiteSpace(skin) ? null : $"/preview/{car}/{skin}.png";
}

public sealed class FakeGameConfigService : IAssettoCorsaGameConfigService
{
    public Task<AssettoCorsaGameConfigModel?> GetAsync() => Task.FromResult<AssettoCorsaGameConfigModel?>(null);

    public Task<bool> SaveAsync(AssettoCorsaGameConfigModel model) => Task.FromResult(true);
}
