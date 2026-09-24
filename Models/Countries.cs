namespace MulticlassRace.Models;

public static class Countries
{
    public static readonly IReadOnlyList<string> All =
    [
        "Argentina",
        "Australia",
        "Austria",
        "Bahrain",
        "Belgium",
        "Brazil",
        "Canada",
        "China",
        "Czech Republic",
        "Denmark",
        "Finland",
        "France",
        "Germany",
        "India",
        "Ireland",
        "Israel",
        "Italy",
        "Japan",
        "Mexico",
        "Monaco",
        "Morocco",
        "Netherlands",
        "New Zealand",
        "Norway",
        "Poland",
        "Portugal",
        "Qatar",
        "Saudi Arabia",
        "Slovakia",
        "South Africa",
        "South Korea",
        "Spain",
        "Sweden",
        "Switzerland",
        "Thailand",
        "Turkey",
        "United Arab Emirates",
        "United Kingdom",
        "United States"
    ];

    public static readonly IReadOnlyDictionary<string, string> Flags = new Dictionary<string, string>
    {
        ["Argentina"] = "🇦🇷",
        ["Australia"] = "🇦🇺",
        ["Austria"] = "🇦🇹",
        ["Bahrain"] = "🇧🇭",
        ["Belgium"] = "🇧🇪",
        ["Brazil"] = "🇧🇷",
        ["Canada"] = "🇨🇦",
        ["China"] = "🇨🇳",
        ["Czech Republic"] = "🇨🇿",
        ["Denmark"] = "🇩🇰",
        ["Finland"] = "🇫🇮",
        ["France"] = "🇫🇷",
        ["Germany"] = "🇩🇪",
        ["India"] = "🇮🇳",
        ["Ireland"] = "🇮🇪",
        ["Israel"] = "🇮🇱",
        ["Italy"] = "🇮🇹",
        ["Japan"] = "🇯🇵",
        ["Mexico"] = "🇲🇽",
        ["Monaco"] = "🇲🇨",
        ["Morocco"] = "🇲🇦",
        ["Netherlands"] = "🇳🇱",
        ["New Zealand"] = "🇳🇿",
        ["Norway"] = "🇳🇴",
        ["Poland"] = "🇵🇱",
        ["Portugal"] = "🇵🇹",
        ["Qatar"] = "🇶🇦",
        ["Saudi Arabia"] = "🇸🇦",
        ["Slovakia"] = "🇸🇰",
        ["South Africa"] = "🇿🇦",
        ["South Korea"] = "🇰🇷",
        ["Spain"] = "🇪🇸",
        ["Sweden"] = "🇸🇪",
        ["Switzerland"] = "🇨🇭",
        ["Thailand"] = "🇹🇭",
        ["Turkey"] = "🇹🇷",
        ["United Arab Emirates"] = "🇦🇪",
        ["United Kingdom"] = "🇬🇧",
        ["United States"] = "🇺🇸"
    };

    public static string GetFlag(string? country)
    {
        return country is not null && Flags.TryGetValue(country, out var flag) ? flag : string.Empty;
    }
}