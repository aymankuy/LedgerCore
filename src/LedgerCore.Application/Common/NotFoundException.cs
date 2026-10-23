namespace LedgerCore.Application.Common;

/// <summary>The requested resource does not exist. The API maps this to 404.</summary>
public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} '{id}' was not found.");
