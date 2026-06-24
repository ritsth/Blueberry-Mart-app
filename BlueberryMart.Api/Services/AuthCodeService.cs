using System.Security.Cryptography;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Security;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Issues + validates the one-time secrets behind email-verification and password-reset links, and
/// sends the corresponding branded email. A secret is 32 random bytes (base64url); only its PBKDF2
/// hash is stored (<see cref="PasswordHasher"/>). There is one active code per (user, purpose); it
/// is single-use (deleted on success).
/// </summary>
public class AuthCodeService(
    BlueberryMartDbContext context,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions) : IAuthCodeService
{
    private static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    private readonly EmailOptions _email = emailOptions.Value;

    public async Task SendVerificationLinkAsync(User user, CancellationToken ct = default)
    {
        var (id, secret) = await GenerateAsync(user.Id, AuthCodePurpose.EmailVerify, VerifyLifetime, ct);
        var link = $"{BaseUrl()}/api/auth/verify-email?uid={id}&t={Uri.EscapeDataString(secret)}";
        await email.SendAsync(user.Email!, "Confirm your Blueberry Mart email",
            EmailTemplates.Verification(link), ct);
    }

    public async Task SendPasswordResetLinkAsync(User user, CancellationToken ct = default)
    {
        var (id, secret) = await GenerateAsync(user.Id, AuthCodePurpose.PasswordReset, ResetLifetime, ct);
        var link = $"{BaseUrl()}/reset-password.html?uid={id}&t={Uri.EscapeDataString(secret)}";
        await email.SendAsync(user.Email!, "Reset your Blueberry Mart password",
            EmailTemplates.PasswordReset(link), ct);
    }

    public async Task<AuthCodeValidation> ValidateAsync(Guid id, string purpose, string secret, CancellationToken ct = default)
    {
        var code = await context.AuthCodes.FirstOrDefaultAsync(c => c.Id == id && c.Purpose == purpose, ct);
        if (code is null || !PasswordHasher.Verify(secret, code.CodeHash, out _))
            return new AuthCodeValidation(AuthCodeStatus.Invalid, Guid.Empty);

        if (code.ExpiresAt <= DateTime.UtcNow)
        {
            context.AuthCodes.Remove(code);
            await context.SaveChangesAsync(ct);
            return new AuthCodeValidation(AuthCodeStatus.Expired, Guid.Empty);
        }

        var userId = code.UserId;
        context.AuthCodes.Remove(code);   // single-use
        await context.SaveChangesAsync(ct);
        return new AuthCodeValidation(AuthCodeStatus.Ok, userId);
    }

    // Upsert the single active code for (user, purpose); returns its id + the plaintext secret.
    private async Task<(Guid Id, string Secret)> GenerateAsync(
        Guid userId, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;

        var code = await context.AuthCodes.FirstOrDefaultAsync(c => c.UserId == userId && c.Purpose == purpose, ct);
        if (code is null)
        {
            code = new AuthCode { Id = Guid.NewGuid(), UserId = userId, Purpose = purpose, CreatedAt = now };
            context.AuthCodes.Add(code);
        }
        code.CodeHash = PasswordHasher.Hash(secret);
        code.ExpiresAt = now.Add(lifetime);
        code.CreatedAt = now;
        await context.SaveChangesAsync(ct);
        return (code.Id, secret);
    }

    private string BaseUrl() => _email.PublicBaseUrl.TrimEnd('/');

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
