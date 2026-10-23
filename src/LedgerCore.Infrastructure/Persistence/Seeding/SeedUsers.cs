namespace LedgerCore.Infrastructure.Persistence.Seeding;

public static class SeedUsers
{
    /// <summary>Owns the settlement accounts. It has an unguessable password and never logs in.</summary>
    public const string SystemEmail = "system@ledgercore.local";

    public static readonly string[] DemoEmails = ["alice@demo.local", "bob@demo.local"];
}
