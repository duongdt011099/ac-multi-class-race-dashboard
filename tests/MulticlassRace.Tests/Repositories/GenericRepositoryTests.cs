using MulticlassRace.Models;
using MulticlassRace.Repositories;
using MulticlassRace.Tests.Support;

namespace MulticlassRace.Tests.Repositories;

public class GenericRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AddGetUpdateDelete_Works()
    {
        using var context = _db.CreateContext();
        var repo = new GenericRepository<Championship>(context);
        var championship = new Championship { ChampionshipId = Guid.NewGuid(), ChampionshipName = "Test" };

        await repo.AddAsync(championship);
        Assert.NotNull(await repo.GetByIdAsync(championship.ChampionshipId));

        championship.ChampionshipName = "Changed";
        await repo.UpdateAsync(championship);
        Assert.Equal("Changed", (await repo.GetByIdAsync(championship.ChampionshipId))!.ChampionshipName);

        await repo.DeleteAsync(championship.ChampionshipId);
        Assert.Null(await repo.GetByIdAsync(championship.ChampionshipId));
    }

    [Fact]
    public async Task Delete_UnknownId_DoesNothing()
    {
        using var context = _db.CreateContext();
        var repo = new GenericRepository<Championship>(context);

        await repo.DeleteAsync(Guid.NewGuid());

        Assert.Empty(await repo.GetAllAsync());
    }
}
