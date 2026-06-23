namespace AspireWithSquad.AuthApi;

/// <summary>
/// Server-authoritative password policy. Client strength meters are hint-only.
/// Failure → <c>400 { code: "WEAK_PASSWORD", message: &lt;reason&gt; }</c>.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static (bool Ok, string? Reason) Validate(string password, string email)
    {
        if (string.IsNullOrEmpty(password))
            return (false, "Password is required.");
        if (password.Length < MinLength)
            return (false, $"Password must be at least {MinLength} characters.");
        if (password.Length > MaxLength)
            return (false, $"Password must be at most {MaxLength} characters.");
        if (password.Trim().Length != password.Length)
            return (false, "Password must not have leading or trailing whitespace.");

        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes < 3)
            return (false, "Password must contain at least 3 of: lowercase, uppercase, digit, symbol.");

        var atIdx = email.IndexOf('@');
        var localPart = atIdx > 0 ? email[..atIdx] : email;
        if (!string.IsNullOrEmpty(localPart) &&
            string.Equals(password, localPart, StringComparison.OrdinalIgnoreCase))
            return (false, "Password must not match the email local-part.");

        return (true, null);
    }
}
