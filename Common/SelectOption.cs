namespace MulticlassRace.Common;

/// <param name="ImageUrl">Optional picture shown next to the label by <c>ImageSelect</c>; plain selects ignore it.</param>
public sealed record SelectOption(string Value, string Label, string? ImageUrl = null);
