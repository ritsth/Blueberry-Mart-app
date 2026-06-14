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
