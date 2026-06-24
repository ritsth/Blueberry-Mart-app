using BlueberryMart.Api.Models.Entities;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>Outcome of validating a link token.</summary>
public enum AuthCodeStatus
{
    Ok,
    Invalid,   // no such code, wrong purpose, or secret mismatch
    Expired
}

public record AuthCodeValidation(AuthCodeStatus Status, Guid UserId);

/// <summary>
/// Issues and validates the one-time secrets behind email-verification and password-reset links.
/// Generation also composes and sends the branded email via <see cref="IEmailSender"/>, keeping the
/// controller thin. Secrets are stored hashed and are single-use.
/// </summary>
public interface IAuthCodeService
{
    /// <summary>Issue a verification link for <paramref name="user"/> and email it.</summary>
    Task SendVerificationLinkAsync(User user, CancellationToken ct = default);

    /// <summary>Issue a password-reset link for <paramref name="user"/> and email it.</summary>
    Task SendPasswordResetLinkAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Validate a link token by its row id, purpose and secret. On success the row is consumed
    /// (deleted) and the owning user id is returned.
    /// </summary>
    Task<AuthCodeValidation> ValidateAsync(Guid id, string purpose, string secret, CancellationToken ct = default);
}
