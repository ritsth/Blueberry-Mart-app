using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ReviewsControllerTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;

    // Minimal 1×1 white PNG used for photo upload tests
    private static readonly byte[] OnePxPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI6QAAAABJRU5ErkJggg==");

    public ReviewsControllerTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    [Fact]
    public async Task SubmitReview_TextOnly_Returns201With10LoyaltyPoints()
    {
        var token   = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(
            _client, token, _factory.DowntownBranchId, _factory.EggsItemId);

        var req = BuildReviewRequest(token, orderId, _factory.EggsItemId,
            rating: 5, comment: "Very fresh eggs!");

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, json.GetProperty("loyaltyPointsEarned").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("imagePath").ValueKind);
    }

    [Fact]
    public async Task SubmitReview_WithImage_Returns201With20LoyaltyPoints()
    {
        var token   = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(
            _client, token, _factory.DowntownBranchId, _factory.BreadItemId);

        var imageContent = new ByteArrayContent(OnePxPng);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var content = new MultipartFormDataContent
        {
            { new StringContent(orderId.ToString()),              "orderId"  },
            { new StringContent(_factory.BreadItemId.ToString()), "itemId"   },
            { new StringContent("4"),                             "rating"   },
            { new StringContent("Lovely crispy crust!"),          "comment"  },
            { imageContent,                                       "image", "review.png" }
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/reviews") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, json.GetProperty("loyaltyPointsEarned").GetInt32());
        Assert.StartsWith("/images/reviews/", json.GetProperty("imagePath").GetString());
    }

    [Fact]
    public async Task SubmitReview_Duplicate_ReturnsConflict()
    {
        var token   = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(
            _client, token, _factory.DowntownBranchId, _factory.MilkItemId);

        Task<HttpResponseMessage> Post() =>
            _client.SendAsync(BuildReviewRequest(token, orderId, _factory.MilkItemId,
                rating: 3, comment: "Good milk."));

        var first  = await Post();
        var second = await Post();

        Assert.Equal(HttpStatusCode.Created,  first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SubmitReview_RatingOutOfRange_ReturnsBadRequest()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        // Rating validation fires before any DB lookup — no real order needed
        var req = BuildReviewRequest(token, Guid.NewGuid(), Guid.NewGuid(),
            rating: 10, comment: "Off the charts!");

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static HttpRequestMessage BuildReviewRequest(
        string token, Guid orderId, Guid itemId, int rating, string comment)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(orderId.ToString()), "orderId" },
            { new StringContent(itemId.ToString()),  "itemId"  },
            { new StringContent(rating.ToString()),  "rating"  },
            { new StringContent(comment),            "comment" }
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/reviews") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }
}
