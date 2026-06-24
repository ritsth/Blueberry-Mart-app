namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// A one-time, single-use secret backing an email link — either an email-verification link or a
/// password-reset link. The plaintext secret is sent only inside the link; we store its PBKDF2 hash
/// (see <see cref="Security.PasswordHasher"/>). There is at most one active row per (user, purpose).
/// </summary>
public class AuthCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // "email_verify" or "password_reset" — see AuthCodePurpose.
    public string Purpose { get; set; } = null!;

    // PBKDF2 hash of the high-entropy secret carried in the link (never the plaintext).
    public string CodeHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The two link purposes, kept as constants to avoid stringly-typed drift.</summary>
public static class AuthCodePurpose
{
    public const string EmailVerify = "email_verify";
    public const string PasswordReset = "password_reset";
}
