using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Security;
using BlueberryMart.Api.Services;
using BlueberryMart.Api.Services.Interfaces;
using BlueberryMart.Api.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    BlueberryMartDbContext context,
    IConfiguration config,
    IGoogleTokenValidator googleValidator,
    IAuthCodeService authCodes) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, out var needsRehash))
            return Unauthorized(new { message = "Invalid email or password." });

        // Email must be confirmed before the account can be used. Tell the app to show its "check
        // your email" state (which has a Resend button) — we deliberately don't auto-send a new link
        // here, since that would invalidate the link already in the user's inbox. (Existing accounts
        // were grandfathered verified.)
        if (!user.EmailVerified)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { requiresVerification = true, email = user.Email });

        // Transparently upgrade a legacy (unsalted-SHA256) hash to PBKDF2 now that we have the
        // plaintext — so old accounts migrate on their next successful login, no reset needed.
        if (needsRehash)
        {
            user.PasswordHash = PasswordHasher.Hash(request.Password);
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        var token = GenerateToken(user.Id, user.Email!, user.Role, user.BranchId);
        return Ok(new { token });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var email = request.Email?.Trim().ToLower() ?? "";
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "A valid email is required." });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        if (await context.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with this email already exists." });

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            if (!PhoneNumber.TryNormalize(request.Phone, out var p))
                return BadRequest(new { message = "Enter a valid phone number (up to 10 digits)." });
            phone = p;
        }

        // Claim flow: if a phone is given and a *guest* account (phone-only, no email yet) exists with
        // it — created at the till — attach email+password to that same record so the customer keeps
        // the loyalty/orders they earned in store. A phone already on a full account is a conflict.
        if (phone is not null)
        {
            var byPhone = await context.Users.FirstOrDefaultAsync(u => u.Phone == phone);
            if (byPhone is not null)
            {
                if (byPhone.Email is not null)
                    return Conflict(new { message = "This phone number is already linked to an account." });

                byPhone.Email = email;
                byPhone.PasswordHash = PasswordHasher.Hash(request.Password);
                byPhone.EmailVerified = false;   // must confirm the email they just attached
                byPhone.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                await authCodes.SendVerificationLinkAsync(byPhone);
                return Ok(new { requiresVerification = true, email = byPhone.Email });
            }
        }

        // Public sign-up always creates a Customer account (storing the phone if one was given).
        // The account starts unverified; a confirmation link is emailed and login is blocked until
        // it's used (see Login). No token is returned here.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Phone = phone,
            Role = "customer",
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await authCodes.SendVerificationLinkAsync(user);
        return Ok(new { requiresVerification = true, email = user.Email });
    }

    // GET /api/auth/verify-email?uid=&t= — the target of the verification link in the email. Marks
    // the email verified and returns a small branded HTML page (this resolves in the browser).
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] Guid uid, [FromQuery] string? t)
    {
        ContentResult Page(string html) => new() { Content = html, ContentType = "text/html; charset=utf-8" };

        if (uid == Guid.Empty || string.IsNullOrWhiteSpace(t))
            return Page(AuthResultPages.VerifyFailed());

        var result = await authCodes.ValidateAsync(uid, AuthCodePurpose.EmailVerify, t);
        if (result.Status != AuthCodeStatus.Ok)
            return Page(AuthResultPages.VerifyFailed());

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == result.UserId);
        if (user is null)
            return Page(AuthResultPages.VerifyFailed());

        if (!user.EmailVerified)
        {
            user.EmailVerified = true;
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        return Page(AuthResultPages.VerifySuccess());
    }

    // GET /api/auth/verification-status?email= — lets the app's "check your email" screen poll for
    // when the link has been opened, so it can auto-advance to sign-in. Unknown emails return false.
    [HttpGet("verification-status")]
    public async Task<IActionResult> VerificationStatus([FromQuery] string? email)
    {
        var normalized = email?.Trim().ToLower() ?? "";
        var verified = await context.Users.AnyAsync(u => u.Email == normalized && u.EmailVerified);
        return Ok(new { verified });
    }

    // POST /api/auth/resend-verification — re-send the verification link. Always 200 (no enumeration).
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        var email = request.Email?.Trim().ToLower() ?? "";
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && !user.EmailVerified && user.DeletedAt is null)
            await authCodes.SendVerificationLinkAsync(user);
        return Ok(new { message = "If that account exists and isn't verified, we've sent a new link." });
    }

    // POST /api/auth/forgot-password — email a reset link. Always 200 (no account enumeration).
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var email = request.Email?.Trim().ToLower() ?? "";
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && user.DeletedAt is null)
            await authCodes.SendPasswordResetLinkAsync(user);
        return Ok(new { message = "If that email has an account, we've sent a reset link." });
    }

    // POST /api/auth/reset-password — called by the hosted reset page with the link's uid+token and
    // the new password. Sets the new password (and verifies the email — control of the inbox proves it).
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        var result = await authCodes.ValidateAsync(request.Uid, AuthCodePurpose.PasswordReset, request.Token ?? "");
        if (result.Status == AuthCodeStatus.Expired)
            return BadRequest(new { message = "This reset link has expired. Please request a new one." });
        if (result.Status != AuthCodeStatus.Ok)
            return BadRequest(new { message = "This reset link is invalid. Please request a new one." });

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == result.UserId);
        if (user is null)
            return BadRequest(new { message = "This reset link is invalid. Please request a new one." });

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.EmailVerified = true;   // they proved control of the inbox
        user.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return Ok(new { message = "Your password has been updated." });
    }

    // POST /api/auth/google — "Continue with Google". The client sends a Google ID token; we verify
    // it server-side, then find-or-link-or-create the account and issue our own JWT (same shape as
    // password login, so the rest of the app is unchanged).
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleSignInRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return BadRequest(new { message = "Missing Google token." });

        var identity = await googleValidator.ValidateAsync(request.IdToken);
        if (identity is null || !identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
            return Unauthorized(new { message = "Could not verify your Google account." });

        var email = identity.Email.ToLower();

        // 1) Returning Google user. 2) Existing email account → link Google to it (email is
        // Google-verified, so this is safe). 3) Otherwise create a new customer.
        var user = await context.Users.FirstOrDefaultAsync(u => u.GoogleId == identity.Subject);
        if (user is null)
        {
            user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is not null)
            {
                user.GoogleId = identity.Subject;
                user.EmailVerified = true;   // Google already verified this address
                user.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            else
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    GoogleId = identity.Subject,
                    PasswordHash = null,
                    Role = "customer",
                    EmailVerified = true,   // Google already verified this address
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }
        }

        var token = GenerateToken(user.Id, user.Email!, user.Role, user.BranchId);
        return Ok(new { token });
    }

    private string GenerateToken(Guid userId, string email, string role, Guid? branchId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Capitalize first letter to match ASP.NET Core role policy ("Customer", "Shareholder")
        var normalizedRole = char.ToUpper(role[0]) + role[1..];

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, normalizedRole),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Branch claim drives back-office branch scoping (staff/manager).
        if (branchId.HasValue)
            claims.Add(new Claim("branch", branchId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
