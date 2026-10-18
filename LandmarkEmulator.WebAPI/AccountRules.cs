using System.Text.RegularExpressions;

namespace LandmarkEmulator.WebAPI
{
    /// <summary>
    /// The sign-up rules every way of creating an account applies: the SRP sign-up (/srp/register: LaunchPad, the account
    /// website), the local plain-text /create and the login server console's "account create". The same two rules are
    /// checked client-side by LaunchPad (Bridge.cs) and the website (docker/web/site/register.js) before anything is sent;
    /// for SRP the server can't see the password, so there only the name is checked here.
    /// </summary>
    public static class AccountRules
    {
        /// <summary>Account names: 3-32 letters, digits or underscores.</summary>
        public static readonly Regex Name = new("^[A-Za-z0-9_]{3,32}$");

        /// <summary>The shortest password accepted.</summary>
        public const int MinPasswordLength = 6;

        /// <summary>Why the name can't be used, or null when it can.</summary>
        public static string? CheckName(string? username)
            => username != null && Name.IsMatch(username) ? null : "The account name must be 3-32 letters, digits or underscores.";

        /// <summary>Why the password can't be used, or null when it can.</summary>
        public static string? CheckPassword(string? password)
            => password != null && password.Length >= MinPasswordLength ? null
                : $"The password needs at least {MinPasswordLength} characters.";

        /// <summary>Both checks, the name first; null when both pass.</summary>
        public static string? Check(string? username, string? password) => CheckName(username) ?? CheckPassword(password);
    }
}
