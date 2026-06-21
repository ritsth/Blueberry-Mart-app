using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class AuthControllerTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public AuthControllerTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ValidCustomer_ReturnsToken()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "customer1@blueberrymart.com",
            password = "customer1_password"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_ValidShareholder_ReturnsToken()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "shareholder1@blueberrymart.com",
            password = "shareholder1_password"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "customer1@blueberrymart.com",
            password = "wrongpassword"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "nobody@blueberrymart.com",
            password = "anypassword"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Register_NewEmail_ReturnsTokenAndCanLogin()
    {
        var email = $"new_{Guid.NewGuid():N}@blueberrymart.com";

        var resp = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("token").GetString()));

        // The freshly created account can log in.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var email = $"dup_{Guid.NewGuid():N}@blueberrymart.com";

        var first  = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        var second = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsBadRequest()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email    = $"short_{Guid.NewGuid():N}@blueberrymart.com",
            password = "123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Register_StoresPbkdf2Hash()
    {
        var email = $"hash_{Guid.NewGuid():N}@blueberrymart.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        Assert.StartsWith("pbkdf2$", await TestHelpers.GetPasswordHashAsync(_factory, email));
    }

    [Fact]
    public async Task Register_WithGuestPhone_ClaimsTheSameAccount()
    {
        // A guest created at the till (phone only, 320 loyalty pts), then someone signs up with that phone.
        var phone = "98" + Random.Shared.Next(10_000_000, 99_999_999);
        var guestId = await TestHelpers.CreateGuestUserAsync(_factory, phone, loyalty: 320);
        var email = $"claim_{Guid.NewGuid():N}@blueberrymart.com";

        var resp = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123", phone });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Same underlying account — loyalty/orders are inherited (not a new row).
        Assert.Equal(guestId, await TestHelpers.GetUserIdByEmailAsync(_factory, email));
        Assert.Equal(320, await TestHelpers.GetLoyaltyPointsAsync(_factory, guestId));

        // The claimed account can now log in.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_PhoneOnFullAccount_ReturnsConflict()
    {
        var phone = "97" + Random.Shared.Next(10_000_000, 99_999_999);
        // First sign-up stores the phone on a full account.
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"first_{Guid.NewGuid():N}@blueberrymart.com", password = "secret123", phone });

        // Second sign-up reusing the same phone is rejected (not a claimable guest).
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"second_{Guid.NewGuid():N}@blueberrymart.com", password = "secret123", phone });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Register_PhoneTooLong_ReturnsBadRequest()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"long_{Guid.NewGuid():N}@blueberrymart.com",
            password = "secret123",
            phone = "123456789012",   // 12 digits > 10
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // Fake validator (registered in the factory) reads tokens shaped "sub|email|verified".
    private Task<HttpResponseMessage> GoogleSignIn(string sub, string email, bool verified = true) =>
        _client.PostAsJsonAsync("/api/auth/google",
            new { idToken = $"{sub}|{email}|{(verified ? "true" : "false")}" });

    [Fact]
    public async Task Google_NewUser_CreatesCustomerAndReturnsToken()
    {
        var sub = $"g-{Guid.NewGuid():N}";
        var email = $"g_{Guid.NewGuid():N}@gmail.com";

        var resp = await GoogleSignIn(sub, email);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("token").GetString()));

        // The account now exists, and signing in again with the same Google id reuses it (no dup).
        var id1 = await TestHelpers.GetUserIdByEmailAsync(_factory, email);
        await GoogleSignIn(sub, email);
        Assert.Equal(id1, await TestHelpers.GetUserIdByEmailAsync(_factory, email));
    }

    [Fact]
    public async Task Google_ExistingEmail_LinksAccountNoDuplicate()
    {
        // A password account already exists with this email.
        var email = $"link_{Guid.NewGuid():N}@gmail.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        var existingId = await TestHelpers.GetUserIdByEmailAsync(_factory, email);

        // Signing in with Google for the same (verified) email links onto the same row.
        var resp = await GoogleSignIn($"g-{Guid.NewGuid():N}", email);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(existingId, await TestHelpers.GetUserIdByEmailAsync(_factory, email));

        // Password still works — linking doesn't disturb the existing credentials.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Google_UnverifiedEmail_ReturnsUnauthorized()
    {
        var resp = await GoogleSignIn($"g-{Guid.NewGuid():N}", $"u_{Guid.NewGuid():N}@gmail.com", verified: false);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Google_InvalidToken_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "not-a-valid-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_LegacyHash_UpgradesToPbkdf2OnSuccess()
    {
        // A user whose stored hash is the legacy unsalted-SHA256 format.
        var email = $"legacy_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "secret123");
        Assert.False((await TestHelpers.GetPasswordHashAsync(_factory, email)).StartsWith("pbkdf2$"));

        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // The legacy hash was transparently rehashed to PBKDF2 on this successful login…
        Assert.StartsWith("pbkdf2$", await TestHelpers.GetPasswordHashAsync(_factory, email));

        // …and the upgraded hash still authenticates.
        var again = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }
}
