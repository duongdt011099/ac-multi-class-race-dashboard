using MulticlassRace.Common;

namespace MulticlassRace.Tests.Common;

public class PagedListTests
{
    [Fact]
    public void Create_SlicesRequestedPage()
    {
        var page = PagedList<int>.Create(Enumerable.Range(1, 25), 2, 10);

        Assert.Equal(Enumerable.Range(11, 10), page.Items);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(10, page.FirstIndex);
    }

    [Fact]
    public void Create_ClampsPageBeyondEnd()
    {
        var page = PagedList<int>.Create(Enumerable.Range(1, 25), 99, 10);

        Assert.Equal(3, page.Page);
        Assert.Equal(5, page.Items.Count);
    }

    [Fact]
    public void Create_ClampsPageBelowOne()
    {
        var page = PagedList<int>.Create(Enumerable.Range(1, 5), 0, 10);

        Assert.Equal(1, page.Page);
    }

    [Fact]
    public void Create_EmptySourceHasOnePage()
    {
        var page = PagedList<int>.Create(Array.Empty<int>(), 3, 10);

        Assert.Equal(1, page.TotalPages);
        Assert.Equal(1, page.Page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public void Create_ExactMultipleHasNoExtraPage()
    {
        Assert.Equal(2, PagedList<int>.Create(Enumerable.Range(1, 20), 1, 10).TotalPages);
    }

    [Fact]
    public void Create_RejectsInvalidPageSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagedList<int>.Create(new[] { 1 }, 1, 0));
    }
}
