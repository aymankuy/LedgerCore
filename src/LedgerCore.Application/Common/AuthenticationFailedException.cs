namespace LedgerCore.Application.Common;

/// <summary>
/// Credentials were rejected. The message is the same whether the email or the password was
/// wrong, so a caller cannot use login to find out which emails are registered. Maps to 401.
/// </summary>
public sealed class AuthenticationFailedException() : Exception("Invalid email or password.");
