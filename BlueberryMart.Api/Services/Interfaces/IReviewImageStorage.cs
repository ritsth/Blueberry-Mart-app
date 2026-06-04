namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Stores a review image and returns a URL/path that can be served back to clients.
/// Implementations: GCS (production) and local filesystem (development/tests).
/// </summary>
public interface IReviewImageStorage
{
    /// <summary>Returns the resolved extension for a content type, or null if unsupported.</summary>
    static string? ResolveExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => null,
    };

    Task<string> SaveAsync(IFormFile image, string extension, CancellationToken ct = default);
}
