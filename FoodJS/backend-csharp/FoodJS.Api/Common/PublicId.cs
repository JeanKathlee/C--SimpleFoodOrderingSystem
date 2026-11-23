using System.Globalization;

namespace FoodJS.Api.Common;

/// <summary>
/// The React frontend and the Node API use string ids such as "M-1001", "O-1001"
/// and "cat-burgers", while the database uses integer keys. This is the single
/// place that converts between the two so every feature formats ids the same way.
/// Parsing is lenient: "M-12", "m-12" and "12" all resolve to 12.
/// </summary>
public static class PublicId
{
    public const string MenuPrefix = "M-";
    public const string OrderPrefix = "O-";
    public const string CategoryPrefix = "cat-";

    public static string Menu(int id) => MenuPrefix + id.ToString(CultureInfo.InvariantCulture);

    public static string Order(int id) => OrderPrefix + id.ToString(CultureInfo.InvariantCulture);

    public static string Category(int id) => CategoryPrefix + id.ToString(CultureInfo.InvariantCulture);

    public static bool TryParseMenu(string? value, out int id) => TryParse(value, MenuPrefix, out id);

    public static bool TryParseOrder(string? value, out int id) => TryParse(value, OrderPrefix, out id);

    public static bool TryParseCategory(string? value, out int id) => TryParse(value, CategoryPrefix, out id);

    private static bool TryParse(string? value, string prefix, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..];
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
