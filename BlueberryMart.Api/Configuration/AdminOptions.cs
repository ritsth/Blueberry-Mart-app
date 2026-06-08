namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Bootstrap admin account. On startup, if no admin user exists and both fields are
/// set, an admin is created from these values. In production the password comes from
/// Secret Manager (ADMIN__PASSWORD); locally from the gitignored
/// appsettings.Development.json. Never commit a real password.
/// </summary>
public class AdminOptions
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}
