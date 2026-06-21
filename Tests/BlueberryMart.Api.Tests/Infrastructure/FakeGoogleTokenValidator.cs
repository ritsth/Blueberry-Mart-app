using BlueberryMart.Api.Security;

namespace BlueberryMart.Api.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IGoogleTokenValidator"/> — real validation calls Google's servers.
/// Tests pass an "idToken" shaped as <c>sub|email|verified</c> (e.g. <c>g-1|a@b.com|true</c>);
/// anything malformed validates as invalid (null), mimicking a rejected token.
/// </summary>
public class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        var parts = idToken.Split('|');
        if (parts.Length != 3 || !bool.TryParse(parts[2], out var verified))
            return Task.FromResult<GoogleIdentity?>(null);
        return Task.FromResult<GoogleIdentity?>(new GoogleIdentity(parts[0], parts[1], verified));
    }
}
