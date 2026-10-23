namespace LedgerCore.Application.Common;

/// <summary>The request clashes with existing state, e.g. a duplicate email. The API maps this to 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
