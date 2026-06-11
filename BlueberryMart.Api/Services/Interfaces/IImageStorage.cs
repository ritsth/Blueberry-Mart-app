namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Stores an uploaded image under a named folder and returns a URL/path that can be served
/// back to clients. Implementations: GCS (production) and local filesystem (development/tests).
/// </summary>
public interface IImageStorage
{
    /// <summary>Returns the resolved extension for a content type, or null if unsupported.</summary>
    static string? ResolveExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => null,
    };

    /// <param name="folder">Logical folder, e.g. "reviews" or "items".</param>
    Task<string> SaveAsync(IFormFile image, string extension, string folder, CancellationToken ct = default);
}
