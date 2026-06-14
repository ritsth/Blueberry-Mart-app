using System.Security.Cryptography;
using System.Text;

namespace BlueberryMart.Api.Security;

/// <summary>
/// Salted, slow password hashing (PBKDF2-HMAC-SHA256) with transparent upgrade of the legacy
/// unsalted-SHA256 hashes. Dependency-free (BCL only). New hashes are stored as
/// <c>pbkdf2$sha256$&lt;iterations&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;</c>.
///
/// <para><see cref="Verify"/> reports <c>needsRehash</c> so callers can re-hash a legacy password
/// on a successful login (rehash-on-login migration) — no mass reset needed.</para>
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 120_000;          // OWASP-range for PBKDF2-HMAC-SHA256
    private const int SaltSize = 16;                 // 128-bit salt
    private const int KeySize = 32;                  // 256-bit derived key
    private const string Prefix = "pbkdf2$sha256$";
    private static readonly HashAlgorithmName Algo = HashAlgorithmName.SHA256;

    /// <summary>Hashes a password with a fresh random salt → a self-describing PBKDF2 string.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algo, KeySize);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// Verifies <paramref name="password"/> against <paramref name="storedHash"/>. Handles both the
    /// new PBKDF2 format and the legacy unsalted-SHA256-base64 format; for a valid legacy hash it
    /// sets <paramref name="needsRehash"/> so the caller can upgrade it on the spot.
    /// </summary>
    public static bool Verify(string password, string? storedHash, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(storedHash)) return false;

        if (storedHash.StartsWith(Prefix, StringComparison.Ordinal))
            return VerifyPbkdf2(password, storedHash);

        // Legacy: unsalted SHA-256, base64. Valid → flag for upgrade to PBKDF2.
        var legacy = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(legacy), Encoding.UTF8.GetBytes(storedHash));
        needsRehash = ok;
        return ok;
    }

    private static bool VerifyPbkdf2(string password, string storedHash)
    {
        // pbkdf2$sha256$<iterations>$<saltB64>$<hashB64>
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || !int.TryParse(parts[2], out var iterations)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algo, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
