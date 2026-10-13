namespace MulticlassRace.ViewModels;

/// <summary>Editable state behind the class setting dialog: the list of classes and its ordering rules.</summary>
public sealed class ClassSettingEditor
{
    public const string DefaultColor = "#52525b";

    public sealed class ItemEdit
    {
        public Guid ClassSettingItemId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Color { get; set; } = DefaultColor;
    }

    public Guid ClassSettingId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public List<ItemEdit> Items { get; } = new();

    public static ClassSettingEditor CreateNew(bool isDefault)
    {
        var editor = new ClassSettingEditor
        {
            ClassSettingId = Guid.NewGuid(),
            IsDefault = isDefault
        };

        editor.AddItem();
        editor.AddItem();

        return editor;
    }

    public static ClassSettingEditor From(ClassSettingModel setting)
    {
        var editor = new ClassSettingEditor
        {
            ClassSettingId = setting.ClassSettingId,
            Name = setting.ClassSettingName,
            IsDefault = setting.IsDefault
        };

        editor.Items.AddRange(setting.Items.Select(i => new ItemEdit
        {
            ClassSettingItemId = i.ClassSettingItemId,
            Name = i.Name,
            Color = i.Color
        }));

        return editor;
    }

    public void AddItem() => Items.Add(new ItemEdit());

    public void RemoveItem(ItemEdit item) => Items.Remove(item);

    public void MoveUp(int index)
    {
        if (index <= 0 || index >= Items.Count)
        {
            return;
        }

        (Items[index - 1], Items[index]) = (Items[index], Items[index - 1]);
    }

    public void MoveDown(int index)
    {
        if (index < 0 || index >= Items.Count - 1)
        {
            return;
        }

        (Items[index + 1], Items[index]) = (Items[index], Items[index + 1]);
    }

    /// <summary>Returns the first problem that stops the setting being saved, or null when it is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Setting name is required.";
        }

        if (Items.Count == 0)
        {
            return "Add at least one class item.";
        }

        if (Items.Any(i => string.IsNullOrWhiteSpace(i.Name)))
        {
            return "All class names must be filled.";
        }

        return null;
    }

    public ClassSettingFormModel ToForm()
    {
        return new ClassSettingFormModel
        {
            ClassSettingId = ClassSettingId,
            ClassSettingName = Name.Trim(),
            IsDefault = IsDefault,
            Items = Items.Select((i, index) => new ClassSettingItemFormModel
            {
                ClassSettingItemId = i.ClassSettingItemId,
                Name = i.Name.Trim(),
                Color = i.Color,
                Order = index + 1
            }).ToList()
        };
    }
}
