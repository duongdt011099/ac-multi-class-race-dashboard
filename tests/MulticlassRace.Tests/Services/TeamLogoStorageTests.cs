using MulticlassRace.Services.Teams;

namespace MulticlassRace.Tests.Services;

public class TeamLogoStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcr-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Func<Stream> Bytes(string content) => () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Save_WritesFileAndReturnsPublicUrl()
    {
        var storage = new TeamLogoStorage(_root);
        var teamId = Guid.NewGuid();

        var url = await storage.SaveAsync(teamId, "Logo.PNG", "image/png", 5, Bytes("hello"));

        Assert.Equal($"/uploads/team-logos/{teamId}.png", url);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_root, "team-logos", $"{teamId}.png")));
    }

    [Fact]
    public async Task Save_ReplacesEarlierLogoWithDifferentExtension()
    {
        var storage = new TeamLogoStorage(_root);
        var teamId = Guid.NewGuid();

        await storage.SaveAsync(teamId, "a.png", "image/png", 1, Bytes("1"));
        await storage.SaveAsync(teamId, "b.jpg", "image/jpeg", 1, Bytes("2"));

        var files = Directory.GetFiles(Path.Combine(_root, "team-logos"));
        Assert.Single(files);
        Assert.EndsWith(".jpg", files[0]);
    }

    [Fact]
    public async Task Save_RejectsOversizedFile()
    {
        var storage = new TeamLogoStorage(_root);

        var ex = await Assert.ThrowsAsync<InvalidLogoException>(() =>
            storage.SaveAsync(Guid.NewGuid(), "a.png", "image/png", TeamLogoStorage.MaxLogoSizeBytes + 1, Bytes("x")));

        Assert.Equal("Logo image must be 2MB or smaller.", ex.Message);
    }

    [Fact]
    public async Task Save_RejectsNonImageType()
    {
        var storage = new TeamLogoStorage(_root);

        await Assert.ThrowsAsync<InvalidLogoException>(() =>
            storage.SaveAsync(Guid.NewGuid(), "a.exe", "application/octet-stream", 1, Bytes("x")));
    }

    [Theory]
    [InlineData("logo.PNG", "image/png", ".png")]
    [InlineData("logo", "image/webp", ".webp")]
    [InlineData("logo", "image/svg+xml", ".svg")]
    [InlineData("logo", "image/jpeg", ".jpg")]
    [InlineData("logo.p?n/g", "image/png", ".png")]
    public void ResolveExtension_NormalisesOrDerivesFromContentType(string name, string contentType, string expected)
    {
        Assert.Equal(expected, TeamLogoStorage.ResolveExtension(name, contentType));
    }
}
