using System.Text.RegularExpressions;

namespace FoodJS.Api.Common;

// Images arrive as a web link (seed menu, Google profile photo) or as an uploaded file
// sent as a base64 data URL. Profile photos allow 2 MB; food images allow 5 MB.
// SVG is refused because it can carry scripts. Same rules as frontend/src/lib/imageUpload.js
public static partial class ImageRules
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MenuMaxBytes = 5 * 1024 * 1024;
    public const string TypeError = "Image must be a PNG, JPG, WebP or GIF file.";
    public const string SizeError = "Image must be 2 MB or smaller.";

    [GeneratedRegex("^data:image/(png|jpeg|webp|gif);base64,(.+)$", RegexOptions.Singleline)]
    private static partial Regex DataUrl();

    // returns an error message, or null when the image is fine (empty means "no image")
    public static string? Check(string? image) => Check(image, MaxBytes);

    public static string? CheckMenuImage(string? image) => Check(image, MenuMaxBytes);

    private static string? Check(string? image, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return null;
        }

        var match = DataUrl().Match(image);
        if (!match.Success)
        {
            return TypeError;
        }

        // cheap size check before decoding: 4 base64 characters hold 3 bytes
        var sizeError = $"Image must be {maxBytes / (1024 * 1024)} MB or smaller.";
        if (match.Groups[2].Length / 4L * 3 > maxBytes + 3)
        {
            return sizeError;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(match.Groups[2].Value);
        }
        catch (FormatException)
        {
            return TypeError;
        }

        if (bytes.Length > maxBytes)
        {
            return sizeError;
        }

        // the file's first bytes must match the type it claims, so a renamed file is refused
        return HasSignature(match.Groups[1].Value, bytes) ? null : TypeError;
    }

    private static bool HasSignature(string type, byte[] b) => type switch
    {
        "png" => b is [0x89, 0x50, 0x4E, 0x47, ..],
        "jpeg" => b is [0xFF, 0xD8, 0xFF, ..],
        "gif" => b is [0x47, 0x49, 0x46, 0x38, ..],
        "webp" => b.Length >= 12 && b is [0x52, 0x49, 0x46, 0x46, ..] && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,
        _ => false,
    };
}
