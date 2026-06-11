using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Saves images to a local folder under the content root. Used for local development and
/// tests where Google Cloud Storage isn't available. Files are served by the static-files
/// middleware from /images/{folder}.
/// </summary>
public class LocalImageStorage(IWebHostEnvironment env) : IImageStorage
{
    public async Task<string> SaveAsync(IFormFile image, string extension, string folder, CancellationToken ct = default)
    {
        var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var uploadDir = Path.Combine(root, "images", folder);
        Directory.CreateDirectory(uploadDir);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(uploadDir, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await image.CopyToAsync(stream, ct);

        return $"/images/{folder}/{fileName}";
    }
}
