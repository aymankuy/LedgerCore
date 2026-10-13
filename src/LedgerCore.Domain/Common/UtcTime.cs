namespace LedgerCore.Domain.Common;

public static class UtcTime
{
    /// <summary>
    /// Rejects non-UTC values and truncates to milliseconds, matching datetime2(3), so an
    /// in-memory value always equals what the database will hand back.
    /// </summary>
    public static DateTime Ensure(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainValidationException("Timestamps must be UTC.");
        }

        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
