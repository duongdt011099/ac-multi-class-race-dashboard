using MulticlassRace.Common;

namespace MulticlassRace.Tests.Common;

public class SortStateTests
{
    private enum Col { Name, Status }

    [Fact]
    public void Toggle_SameColumnFlipsDirection()
    {
        var state = new SortState<Col>(Col.Name);

        state.Toggle(Col.Name);
        Assert.True(state.Descending);

        state.Toggle(Col.Name);
        Assert.False(state.Descending);
    }

    [Fact]
    public void Toggle_NewColumnStartsAscending()
    {
        var state = new SortState<Col>(Col.Name, descending: true);

        state.Toggle(Col.Status);

        Assert.Equal(Col.Status, state.Column);
        Assert.False(state.Descending);
    }

    [Fact]
    public void Apply_OrdersByDirection()
    {
        var state = new SortState<Col>(Col.Name);
        var items = new[] { "b", "c", "a" };

        Assert.Equal(new[] { "a", "b", "c" }, state.Apply(items, x => x));

        state.Toggle(Col.Name);
        Assert.Equal(new[] { "c", "b", "a" }, state.Apply(items, x => x));
    }

    [Fact]
    public void Apply_UsesComparer()
    {
        var state = new SortState<Col>(Col.Name);

        var result = state.Apply(new[] { "b", "A", "c" }, x => x, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new[] { "A", "b", "c" }, result);
    }
}
