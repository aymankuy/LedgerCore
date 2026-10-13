namespace LedgerCore.Domain.Common;

/// <summary>
/// A business rule was violated. The API maps these to 4xx responses; they never indicate a bug.
/// </summary>
public abstract class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable, machine-readable error code surfaced to API clients.</summary>
    public string Code { get; } = code;
}

/// <summary>Input that is structurally valid but violates a domain invariant.</summary>
public sealed class DomainValidationException(string message)
    : DomainException("validation_failed", message);
