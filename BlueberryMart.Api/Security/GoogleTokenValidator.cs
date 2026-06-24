using Google.Apis.Auth;

namespace BlueberryMart.Api.Security;

/// <summary>The verified identity from a Google ID token.</summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified);

/// <summary>
/// Validates a Google ID token from the mobile client. Behind an interface so tests can swap in a
/// fake (real validation calls Google's servers / verifies signatures against Google's public keys).
/// </summary>
public interface IGoogleTokenValidator
{
    /// <summary>Returns the verified identity, or null if the token is invalid/untrusted.</summary>
    Task<GoogleIdentity?> ValidateAsync(string idToken);
}

public class GoogleTokenValidator(IConfiguration config) : IGoogleTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        var clientId = config["Google:WebClientId"];
        if (string.IsNullOrWhiteSpace(clientId)) return null;

        try
        {
            // Verifies the signature, expiry, issuer, and that the token was minted for us (audience).
            // ValidateAsync may fetch Google's signing certs over HTTP; cap it so a slow/unreachable
            // Google can't hang the sign-in request thread indefinitely.
            var settings = new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings)
                .WaitAsync(TimeSpan.FromSeconds(10));
            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
