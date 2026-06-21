using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Security;
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
    IGoogleTokenValidator googleValidator) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, out var needsRehash))
            return Unauthorized(new { message = "Invalid email or password." });

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
                byPhone.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return Ok(new { token = GenerateToken(byPhone.Id, byPhone.Email!, byPhone.Role, branchId: null) });
            }
        }

        // Public sign-up always creates a Customer account (storing the phone if one was given).
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Phone = phone,
            Role = "customer",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Public sign-up always creates a customer, who is never tied to a branch.
        var token = GenerateToken(user.Id, user.Email!, user.Role, branchId: null);
        return Ok(new { token });
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
