using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace LedgerCore.Application.Accounts.GetStatement;

/// <summary>
/// The position of a statement line in the statement's order (CreatedAtUtc, then EntryId). The
/// next page starts strictly after it, so a page costs the same however deep it is, and lines
/// posted meanwhile never shift a page the way an OFFSET would. Clients treat the string as opaque.
/// </summary>
public sealed record StatementCursor(DateTime AfterUtc, long AfterEntryId)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{AfterUtc.Ticks}:{AfterEntryId}")));

    public static bool TryDecode(string value, out StatementCursor cursor)
    {
        cursor = null!;
        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value)).Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTime.MaxValue.Ticks
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var entryId))
            {
                cursor = new StatementCursor(new DateTime(ticks, DateTimeKind.Utc), entryId);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
