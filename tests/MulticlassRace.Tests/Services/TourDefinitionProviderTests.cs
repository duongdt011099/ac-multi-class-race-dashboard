using Microsoft.Extensions.Logging.Abstractions;
using MulticlassRace.Services.Tour;

namespace MulticlassRace.Tests.Services;

public class TourDefinitionProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcr-tour-" + Guid.NewGuid().ToString("N"));

    public TourDefinitionProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private TourDefinitionProvider ProviderFor(string fileName) =>
        new(Path.Combine(_root, fileName), NullLogger<TourDefinitionProvider>.Instance);

    private string Write(string json)
    {
        var name = Guid.NewGuid().ToString("N") + ".json";
        File.WriteAllText(Path.Combine(_root, name), json);
        return name;
    }

    [Fact]
    public async Task ShippedScript_IsValid_AndEveryStepCanBeShown()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "Tour", "tour-steps.json");
        var provider = new TourDefinitionProvider(shipped, NullLogger<TourDefinitionProvider>.Instance);

        var tour = await provider.LoadAsync();

        Assert.NotNull(tour);
        Assert.NotEmpty(tour!.Steps);
        Assert.All(tour.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Id));
            Assert.False(string.IsNullOrWhiteSpace(step.Message));
        });
        Assert.Equal(tour.Steps.Count, tour.Steps.Select(step => step.Id).Distinct().Count());
    }

    [Fact]
    public async Task Load_AcceptsCommentsAndTrailingCommas()
    {
        var name = Write("""
            {
              // hand edited
              "steps": [
                { "id": "a", "message": "Hello", "route": "teams", "target": "[data-tour='x']", "passThrough": true },
              ],
            }
            """);

        var tour = await ProviderFor(name).LoadAsync();

        var step = Assert.Single(tour!.Steps);
        Assert.Equal("teams", step.Route);
        Assert.True(step.PassThrough);
    }

    [Fact]
    public async Task Load_DropsStepsWithoutAMessage()
    {
        var name = Write("""{ "steps": [ { "id": "a", "message": "" }, { "id": "b", "message": "Keep me" } ] }""");

        var tour = await ProviderFor(name).LoadAsync();

        Assert.Equal("b", Assert.Single(tour!.Steps).Id);
    }

    [Fact]
    public async Task Load_ReturnsNull_WhenFileIsMissing_Broken_OrEmpty()
    {
        Assert.Null(await ProviderFor("missing.json").LoadAsync());
        Assert.Null(await ProviderFor(Write("{ not json")).LoadAsync());
        Assert.Null(await ProviderFor(Write("""{ "steps": [] }""")).LoadAsync());
    }
}
