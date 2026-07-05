namespace BlueberryMart.Api.Models.DTOs;

/// <summary>
/// The per-request revocation snapshot checked in the JwtBearer <c>OnTokenValidated</c> hook:
/// is the account banned, deleted, or has its password been reset since the token was issued.
/// Cached briefly per user (see <c>RedisOptions.AuthStatusTtlSeconds</c>) and evicted immediately
/// whenever any of these fields changes, so revocation stays effectively instant.
/// </summary>
public record AuthStatus(bool IsBanned, DateTime? DeletedAt, DateTime? PasswordChangedAt);
