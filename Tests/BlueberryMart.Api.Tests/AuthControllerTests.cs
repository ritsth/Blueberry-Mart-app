using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
    public async Task Register_NewEmail_RequiresVerification_NoToken()
    {
        var email = $"new_{Guid.NewGuid():N}@blueberrymart.com";

        var resp = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // Registration no longer logs the user in — it asks them to verify, and returns no token.
        Assert.True(json.GetProperty("requiresVerification").GetBoolean());
        Assert.False(json.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task Login_UnverifiedAccount_ReturnsForbiddenRequiresVerification()
    {
        var email = $"unverified_{Guid.NewGuid():N}@blueberrymart.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("requiresVerification").GetBoolean());
    }

    [Fact]
    public async Task VerifyEmail_ValidLink_VerifiesAndEnablesLogin()
    {
        var email = $"verify_{Guid.NewGuid():N}@blueberrymart.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        // Tap the link from the (captured) verification email.
        var link = _factory.Emails.LastLink(email);
        Assert.NotNull(link);
        var verify = await _client.GetAsync(
            $"/api/auth/verify-email?uid={link!.Value.Uid}&t={Uri.EscapeDataString(link.Value.Token)}");
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        // Now the account can log in.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_InvalidLink_DoesNotVerify()
    {
        var email = $"badlink_{Guid.NewGuid():N}@blueberrymart.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        // A wrong token for a real uid is rejected (page returns 200 HTML, but no verification happens).
        var link = _factory.Emails.LastLink(email);
        Assert.NotNull(link);
        await _client.GetAsync($"/api/auth/verify-email?uid={link!.Value.Uid}&t=not-the-real-secret");

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [Fact]
    public async Task ForgotThenReset_ChangesPassword_OldOneStopsWorking()
    {
        // A verified, logged-in customer.
        var email = $"reset_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.RegisterAndVerifyAsync(_factory, _client, email, "oldpass123");

        // Request a reset and follow the link the page would submit.
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var link = _factory.Emails.LastLink(email);
        Assert.NotNull(link);
        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { uid = link!.Value.Uid, token = link.Value.Token, newPassword = "newpass456" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // New password works; old one no longer does.
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "newpass456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "oldpass123" })).StatusCode);
    }

    [Fact]
    public async Task VerificationStatus_ReflectsWhetherEmailIsVerified()
    {
        var email = $"status_{Guid.NewGuid():N}@blueberrymart.com";
        await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123" });

        // Unverified right after registration…
        var before = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/auth/verification-status?email={Uri.EscapeDataString(email)}");
        Assert.False(before.GetProperty("verified").GetBoolean());

        // …and true once the link is opened.
        var link = _factory.Emails.LastLink(email);
        Assert.NotNull(link);
        await _client.GetAsync($"/api/auth/verify-email?uid={link!.Value.Uid}&t={Uri.EscapeDataString(link.Value.Token)}");

        var after = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/auth/verification-status?email={Uri.EscapeDataString(email)}");
        Assert.True(after.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturnsOk()
    {
        // No account enumeration: an unknown email gets the same 200 as a known one.
        var resp = await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = $"ghost_{Guid.NewGuid():N}@blueberrymart.com" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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

        // The newly-attached email must be verified before the claimed account can log in.
        var link = _factory.Emails.LastLink(email);
        Assert.NotNull(link);
        await _client.GetAsync($"/api/auth/verify-email?uid={link!.Value.Uid}&t={Uri.EscapeDataString(link.Value.Token)}");

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

    [Fact]
    public async Task Login_TooManyAttempts_ReturnsTooManyRequests()
    {
        // A separate host with a low limit (own limiter state) so the shared client is unaffected.
        using var strict = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "2" })));
        var client = strict.CreateClient();
        var body = new { email = "nobody@example.com", password = "wrong" };

        // Rate limiting runs before the action, so invalid creds still count toward the limit.
        await client.PostAsJsonAsync("/api/auth/login", body);   // 1 (401)
        await client.PostAsJsonAsync("/api/auth/login", body);   // 2 (401)
        var third = await client.PostAsJsonAsync("/api/auth/login", body); // 3 → throttled

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
