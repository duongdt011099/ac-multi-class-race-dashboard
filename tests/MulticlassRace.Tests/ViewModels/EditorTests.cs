using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.ViewModels;

public class ClassSettingEditorTests
{
    [Fact]
    public void CreateNew_StartsWithTwoBlankItems()
    {
        var editor = ClassSettingEditor.CreateNew(isDefault: true);

        Assert.Equal(2, editor.Items.Count);
        Assert.True(editor.IsDefault);
        Assert.NotEqual(Guid.Empty, editor.ClassSettingId);
    }

    [Fact]
    public void Validate_ReportsFirstProblem()
    {
        var editor = ClassSettingEditor.CreateNew(false);
        Assert.Equal("Setting name is required.", editor.Validate());

        editor.Name = "X";
        Assert.Equal("All class names must be filled.", editor.Validate());

        editor.Items.Clear();
        Assert.Equal("Add at least one class item.", editor.Validate());

        editor.AddItem();
        editor.Items[0].Name = "GT3";
        Assert.Null(editor.Validate());
    }

    [Fact]
    public void MoveUpAndDown_SwapNeighboursAndIgnoreEdges()
    {
        var editor = new ClassSettingEditor();
        foreach (var name in new[] { "A", "B", "C" })
        {
            editor.Items.Add(new ClassSettingEditor.ItemEdit { Name = name });
        }

        editor.MoveUp(0);
        editor.MoveDown(2);
        Assert.Equal(new[] { "A", "B", "C" }, editor.Items.Select(i => i.Name));

        editor.MoveDown(0);
        Assert.Equal(new[] { "B", "A", "C" }, editor.Items.Select(i => i.Name));

        editor.MoveUp(2);
        Assert.Equal(new[] { "B", "C", "A" }, editor.Items.Select(i => i.Name));
    }

    [Fact]
    public void ToForm_TrimsNamesAndNumbersOrderFromOne()
    {
        var editor = new ClassSettingEditor { Name = " WEC ", IsDefault = true };
        editor.Items.Add(new ClassSettingEditor.ItemEdit { Name = " Hyper ", Color = "#fff" });
        editor.Items.Add(new ClassSettingEditor.ItemEdit { Name = "GT3" });

        var form = editor.ToForm();

        Assert.Equal("WEC", form.ClassSettingName);
        Assert.Equal(new[] { 1, 2 }, form.Items.Select(i => i.Order));
        Assert.Equal("Hyper", form.Items[0].Name);
    }

    [Fact]
    public void From_CopiesExistingItems()
    {
        var model = new ClassSettingModel
        {
            ClassSettingId = Guid.NewGuid(),
            ClassSettingName = "WEC",
            IsDefault = true,
            Items = { new ClassSettingItemModel { ClassSettingItemId = Guid.NewGuid(), Name = "GT3", Color = "#0f0", Order = 1 } }
        };

        var editor = ClassSettingEditor.From(model);

        Assert.Equal("WEC", editor.Name);
        Assert.Equal("GT3", editor.Items.Single().Name);
        Assert.Equal(model.Items[0].ClassSettingItemId, editor.Items[0].ClassSettingItemId);
    }
}

public class PointSettingFormsTests
{
    [Fact]
    public void AddEntry_UsesOnePastHighestPosition()
    {
        var form = PointSettingForms.CreateNew(false);
        form.Entries.Add(new PointSettingEntryModel { Position = "7" });
        form.Entries.Add(new PointSettingEntryModel { Position = "abc" });

        form.AddEntry();

        Assert.Equal("8", form.Entries[^1].Position);
    }

    [Fact]
    public void AddEntry_OnEmptyListStartsAtOne()
    {
        var form = PointSettingForms.CreateNew(false);
        form.Entries.Clear();

        form.AddEntry();

        Assert.Equal("1", form.Entries.Single().Position);
    }

    [Fact]
    public void From_OrdersPositionsNumerically()
    {
        var model = new PointSettingModel
        {
            SettingId = Guid.NewGuid(),
            SettingName = "S",
            Config = new Dictionary<string, int> { ["10"] = 1, ["2"] = 18, ["1"] = 25 }
        };

        var form = PointSettingForms.From(model);

        Assert.Equal(new[] { "1", "2", "10" }, form.Entries.Select(e => e.Position));
        Assert.Equal("18", form.Entries[1].Points);
    }

    [Fact]
    public void FormatPreview_JoinsOrderedPairs()
    {
        var preview = PointSettingForms.FormatPreview(new Dictionary<string, int> { ["2"] = 18, ["1"] = 25 });

        Assert.Equal("1:25 › 2:18", preview);
    }
}
