using System.Text.RegularExpressions;

namespace LandmarkLocal;

/// <summary>Same account rules as LaunchPad (Bridge.CheckAccount).</summary>
public static class AccountRules
{
    static readonly Regex AccountName = new("^[A-Za-z0-9_]{3,32}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidAccountName(string? account)
        => !string.IsNullOrEmpty(account) && AccountName.IsMatch(account);

    /// <summary>
    /// Validates account name and, when <paramref name="passwordRequired"/>, password length.
    /// Throws <see cref="InvalidOperationException"/> with a plain message.
    /// </summary>
    public static void Check(string account, string? password, bool passwordRequired)
    {
        if (!IsValidAccountName(account))
            throw new InvalidOperationException("Account name: 3 to 32 letters, digits or _.");
        if (passwordRequired && (password == null || password.Length < 6))
            throw new InvalidOperationException("The password needs at least 6 characters.");
    }
}
