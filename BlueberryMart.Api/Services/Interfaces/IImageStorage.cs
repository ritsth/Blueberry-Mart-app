namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Stores an uploaded image under a named folder and returns a URL/path that can be served
/// back to clients. Implementations: GCS (production) and local filesystem (development/tests).
/// </summary>
public interface IImageStorage
{
    /// <summary>Largest accepted upload. Guards disk/bandwidth and parsing cost.</summary>
    const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Returns the resolved extension for a content type, or null if unsupported.</summary>
    static string? ResolveExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => null,
    };

    /// <summary>
    /// Validates an uploaded image before storing it: present, within <see cref="MaxImageBytes"/>,
    /// a supported content type, AND whose leading bytes actually match that type (so a renamed
    /// non-image sent with <c>Content-Type: image/jpeg</c> is rejected). Returns the resolved
    /// extension on success, or a user-facing error message.
    /// </summary>
    static async Task<(string? Extension, string? Error)> ValidateImageAsync(
        IFormFile? image, CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
            return (null, "No image provided.");
        if (image.Length > MaxImageBytes)
            return (null, "Image must be 5 MB or smaller.");

        var ext = ResolveExtension(image.ContentType);
        if (ext is null)
            return (null, "Only JPEG, PNG, and WebP images are allowed.");

        var header = new byte[12];
        await using var stream = image.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        bool ok = ext switch
        {
            ".jpg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E
                      && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A
                      && header[6] == 0x1A && header[7] == 0x0A,
            ".webp" => read >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I'
                       && header[2] == (byte)'F' && header[3] == (byte)'F' && header[8] == (byte)'W'
                       && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
            _ => false,
        };
        return ok ? (ext, null) : (null, "The file doesn't look like a valid image.");
    }

    /// <param name="folder">Logical folder, e.g. "reviews" or "items".</param>
    Task<string> SaveAsync(IFormFile image, string extension, string folder, CancellationToken ct = default);
}
