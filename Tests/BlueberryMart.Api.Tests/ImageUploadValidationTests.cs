using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

/// <summary>
/// Covers the upload hardening on the item-image endpoint (size cap + magic-byte sniffing), so a
/// renamed non-image or an oversized file is rejected even when it carries an image content-type.
/// </summary>
[Collection("Integration")]
public class ImageUploadValidationTests
{
    private readonly HttpClient _client;

    // A real 1×1 PNG (valid magic bytes) — the happy path.
    private static readonly byte[] OnePxPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI6QAAAABJRU5ErkJggg==");

    public ImageUploadValidationTests(BlueberryMartApiFactory factory) => _client = factory.CreateClient();

    private async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string contentType, string fileName)
    {
        var token = await TestHelpers.GetAdminTokenAsync(_client);
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var content = new MultipartFormDataContent { { part, "image", fileName } };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/manage/image") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task UploadImage_ValidPng_ReturnsUrl()
    {
        var resp = await UploadAsync(OnePxPng, "image/png", "item.png");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("/images/items/", json.GetProperty("url").GetString());
    }

    [Fact]
    public async Task UploadImage_RenamedNonImage_ReturnsBadRequest()
    {
        // Plain text masquerading as a PNG via the content-type header.
        var fake = Encoding.UTF8.GetBytes("this is definitely not an image");

        var resp = await UploadAsync(fake, "image/png", "evil.png");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UploadImage_OverSizeLimit_ReturnsBadRequest()
    {
        // 6 MB (over the 5 MB cap). Magic bytes don't matter — size is checked first.
        var big = new byte[6 * 1024 * 1024];

        var resp = await UploadAsync(big, "image/jpeg", "huge.jpg");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
