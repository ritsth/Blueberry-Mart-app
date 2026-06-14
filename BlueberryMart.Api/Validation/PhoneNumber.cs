namespace BlueberryMart.Api.Validation;

/// <summary>Phone-number normalization + the max-10-digit rule, shared by the till guest-create
/// and the account-claim sign-up path.</summary>
public static class PhoneNumber
{
    public const int MaxDigits = 10;

    /// <summary>Strips everything but digits; valid when the result is 1..10 digits.
    /// <paramref name="normalized"/> is the digits-only form to store/compare.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        return normalized.Length is >= 1 and <= MaxDigits;
    }
}
