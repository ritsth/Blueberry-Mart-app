using BlueberryMart.Api.Services.Interfaces;
using Google.Cloud.Storage.V1;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Uploads images to a Google Cloud Storage bucket and returns the public URL.
/// Authenticates via Application Default Credentials (the Cloud Run runtime service
/// account in production, the developer's gcloud ADC locally).
/// </summary>
public class GcsImageStorage : IImageStorage
{
    private readonly string _bucket;
    private readonly StorageClient _client;

    public GcsImageStorage(IConfiguration config)
    {
        _bucket = config["Gcs:BucketName"]
            ?? throw new InvalidOperationException("Gcs:BucketName is not configured.");
        _client = StorageClient.Create();
    }

    public async Task<string> SaveAsync(IFormFile image, string extension, string folder, CancellationToken ct = default)
    {
        var objectName = $"{folder}/{Guid.NewGuid()}{extension}";

        await using var stream = image.OpenReadStream();
        await _client.UploadObjectAsync(
            _bucket, objectName, image.ContentType, stream, cancellationToken: ct);

        // Bucket grants public object read, so the canonical public URL is stable
        return $"https://storage.googleapis.com/{_bucket}/{objectName}";
    }
}
