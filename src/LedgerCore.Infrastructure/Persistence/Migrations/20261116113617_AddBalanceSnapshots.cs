using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 4: balance snapshots (ADR 0009). A refresh folds every entry up to a watermark into
    /// AccountBalanceSnapshots. A balance read is that snapshot plus the entries after the
    /// watermark, which are a short range at the end of the clustered index. The cost of a read
    /// no longer grows with the account's history.
    /// </summary>
    public partial class AddBalanceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // fn_AccountBalance reads the watermark, a snapshot and the newer entries in one
            // statement. That statement sees a single point in time only under row versioning.
            // Azure SQL has it on by default; this makes local and test databases match it.
            // ALTER DATABASE cannot run inside the migration's transaction.
            migrationBuilder.Sql("""
                IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0
                    EXEC (N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;');
                """, suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "AccountBalanceSnapshots",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBalanceSnapshots", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_AccountBalanceSnapshots_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BalanceSnapshotRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FromEntryId = table.Column<long>(type: "bigint", nullable: false),
                    ToEntryId = table.Column<long>(type: "bigint", nullable: false),
                    AccountsUpdated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceSnapshotRuns", x => x.Id);
                });

            // Identity values are handed out at INSERT, but transactions commit in any order. An
            // entry with a lower Id can therefore still be uncommitted when MAX(Id) is read, and a
            // watermark taken at that moment would skip it forever. Every posting holds this
            // lock in shared mode until it commits. The refresh takes it exclusively for the
            // instant it reads MAX(Id), when no posting is in flight.
            migrationBuilder.Sql("""
                ALTER PROCEDURE dbo.usp_PostLedgerTransaction
                    @TransactionId uniqueidentifier,
                    @Type          tinyint,
                    @ReferenceId   nvarchar(64),
                    @Description   nvarchar(256),
                    @CreatedAtUtc  datetime2(3),
                    @Entries       dbo.LedgerEntryList READONLY,
                    @EventId       uniqueidentifier,
                    @EventType     nvarchar(256),
                    @EventPayload  nvarchar(max)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    BEGIN TRANSACTION;

                    -- Held until the outermost transaction commits (see usp_RefreshBalanceSnapshots).
                    DECLARE @lock int;
                    EXEC @lock = sp_getapplock @Resource = N'LedgerCore.LedgerPosting', @LockMode = N'Shared', @LockOwner = N'Transaction';
                    IF @lock < 0
                        THROW 50010, 'Could not acquire the ledger posting lock.', 1;

                    INSERT INTO dbo.LedgerTransactions (Id, Type, ReferenceId, Description, CreatedAtUtc)
                    VALUES (@TransactionId, @Type, @ReferenceId, @Description, @CreatedAtUtc);

                    INSERT INTO dbo.LedgerEntries (TransactionId, AccountId, Direction, Amount, CreatedAtUtc)
                    SELECT @TransactionId, e.AccountId, e.Direction, e.Amount, @CreatedAtUtc
                    FROM @Entries AS e;

                    INSERT INTO dbo.OutboxMessages (Id, Type, Payload, OccurredAtUtc)
                    VALUES (@EventId, @EventType, @EventPayload, @CreatedAtUtc);

                    COMMIT TRANSACTION;
                END
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_RefreshBalanceSnapshots
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    DECLARE @StartedAtUtc datetime2(3) = SYSUTCDATETIME(),
                            @FromEntryId  bigint,
                            @ToEntryId    bigint,
                            @Accounts     int = 0,
                            @lock         int;

                    BEGIN TRANSACTION;

                    -- One refresh at a time. A second caller gets an empty result at once instead
                    -- of folding the same entries in twice.
                    EXEC @lock = sp_getapplock @Resource = N'LedgerCore.BalanceSnapshotRefresh', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
                    IF @lock < 0
                    BEGIN
                        ROLLBACK TRANSACTION;
                        SELECT TOP (0) Id, StartedAtUtc, FinishedAtUtc, FromEntryId, ToEntryId, AccountsUpdated FROM dbo.BalanceSnapshotRuns;
                        RETURN;
                    END

                    SET @FromEntryId = COALESCE((SELECT TOP (1) ToEntryId FROM dbo.BalanceSnapshotRuns ORDER BY Id DESC), 0);

                    -- The cut. Once the exclusive lock is granted, every posting that started
                    -- earlier has committed, and every later one gets a higher Id. Postings queue
                    -- only for as long as this single read takes.
                    EXEC @lock = sp_getapplock @Resource = N'LedgerCore.LedgerPosting', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
                    IF @lock < 0
                        THROW 50011, 'Timed out waiting for in-flight postings; the next run will try again.', 1;

                    SET @ToEntryId = COALESCE((SELECT MAX(Id) FROM dbo.LedgerEntries), 0);

                    EXEC sp_releaseapplock @Resource = N'LedgerCore.LedgerPosting', @LockOwner = N'Transaction';

                    IF @ToEntryId > @FromEntryId
                    BEGIN
                        -- Aggregated first, on its own: the scan of new entries never runs while
                        -- this transaction holds locks on snapshot rows.
                        SELECT AccountId, SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END) AS Delta
                        INTO #Delta
                        FROM dbo.LedgerEntries
                        WHERE Id > @FromEntryId AND Id <= @ToEntryId
                        GROUP BY AccountId;

                        UPDATE s
                        SET Balance = s.Balance + d.Delta, UpdatedAtUtc = SYSUTCDATETIME()
                        FROM dbo.AccountBalanceSnapshots AS s
                        JOIN #Delta AS d ON d.AccountId = s.AccountId;
                        SET @Accounts = @@ROWCOUNT;

                        INSERT INTO dbo.AccountBalanceSnapshots (AccountId, Balance, UpdatedAtUtc)
                        SELECT d.AccountId, d.Delta, SYSUTCDATETIME()
                        FROM #Delta AS d
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.AccountBalanceSnapshots AS s WHERE s.AccountId = d.AccountId);
                        SET @Accounts += @@ROWCOUNT;
                    END
                    ELSE
                        SET @ToEntryId = @FromEntryId;

                    -- The new watermark and the snapshots it describes commit together.
                    INSERT INTO dbo.BalanceSnapshotRuns (StartedAtUtc, FinishedAtUtc, FromEntryId, ToEntryId, AccountsUpdated)
                    OUTPUT inserted.Id, inserted.StartedAtUtc, inserted.FinishedAtUtc, inserted.FromEntryId, inserted.ToEntryId, inserted.AccountsUpdated
                    VALUES (@StartedAtUtc, SYSUTCDATETIME(), @FromEntryId, @ToEntryId, @Accounts);

                    COMMIT TRANSACTION;
                END
                """);

            // Still the single definition of a balance. The watermark, the snapshot and the
            // entries after it are read in one statement, so under READ_COMMITTED_SNAPSHOT
            // they come from one consistent point in time (ADR 0009). The hint pins the
            // clustered index. Its range Id > watermark is the newest few pages of the table,
            // while the per-account index would read the account's whole history to apply
            // the same filter. Without the hint the optimizer cannot tell the two apart,
            // because it does not know the watermark when it compiles.
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION dbo.fn_AccountBalance (@AccountId uniqueidentifier)
                RETURNS TABLE
                AS
                RETURN
                    SELECT COALESCE((SELECT s.Balance FROM dbo.AccountBalanceSnapshots AS s WHERE s.AccountId = @AccountId), 0)
                         + COALESCE((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
                                     FROM dbo.LedgerEntries AS e WITH (INDEX(1), FORCESEEK)
                                     WHERE e.Id > w.ToEntryId AND e.AccountId = @AccountId), 0) AS Balance
                    FROM (SELECT COALESCE((SELECT TOP (1) r.ToEntryId FROM dbo.BalanceSnapshotRuns AS r ORDER BY r.Id DESC), 0) AS ToEntryId) AS w;
                """);

            // The opening balance is now the current balance minus the entries since @FromUtc.
            // For a recent window that is a few thousand rows instead of the account's whole history.
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetAccountStatement
                    @AccountId uniqueidentifier,
                    @FromUtc   datetime2(3),
                    @ToUtc     datetime2(3)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    -- One statement, so the balance and the subtracted entries are read at the
                    -- same point in time.
                    DECLARE @OpeningBalance decimal(19, 4) =
                        (SELECT b.Balance
                              - COALESCE((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
                                          FROM dbo.LedgerEntries AS e
                                          WHERE e.AccountId = @AccountId AND e.CreatedAtUtc >= @FromUtc), 0)
                         FROM dbo.fn_AccountBalance(@AccountId) AS b);

                    SELECT Id AS AccountId, Currency, @OpeningBalance AS OpeningBalance
                    FROM dbo.Accounts
                    WHERE Id = @AccountId;

                    SELECT e.Id AS EntryId,
                           e.TransactionId,
                           t.Type,
                           t.ReferenceId,
                           t.Description,
                           s.Amount,
                           @OpeningBalance + SUM(s.Amount) OVER (ORDER BY e.CreatedAtUtc, e.Id ROWS UNBOUNDED PRECEDING) AS BalanceAfter,
                           e.CreatedAtUtc
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
                    CROSS APPLY (SELECT CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END AS Amount) AS s
                    WHERE e.AccountId = @AccountId
                      AND e.CreatedAtUtc >= @FromUtc
                      AND e.CreatedAtUtc < @ToUtc
                    ORDER BY e.CreatedAtUtc, e.Id;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetAccountStatement
                    @AccountId uniqueidentifier,
                    @FromUtc   datetime2(3),
                    @ToUtc     datetime2(3)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @OpeningBalance decimal(19, 4) =
                        (SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0)
                         FROM dbo.LedgerEntries
                         WHERE AccountId = @AccountId AND CreatedAtUtc < @FromUtc);

                    SELECT Id AS AccountId, Currency, @OpeningBalance AS OpeningBalance
                    FROM dbo.Accounts
                    WHERE Id = @AccountId;

                    SELECT e.Id AS EntryId,
                           e.TransactionId,
                           t.Type,
                           t.ReferenceId,
                           t.Description,
                           s.Amount,
                           @OpeningBalance + SUM(s.Amount) OVER (ORDER BY e.CreatedAtUtc, e.Id ROWS UNBOUNDED PRECEDING) AS BalanceAfter,
                           e.CreatedAtUtc
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
                    CROSS APPLY (SELECT CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END AS Amount) AS s
                    WHERE e.AccountId = @AccountId
                      AND e.CreatedAtUtc >= @FromUtc
                      AND e.CreatedAtUtc < @ToUtc
                    ORDER BY e.CreatedAtUtc, e.Id;
                END
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION dbo.fn_AccountBalance (@AccountId uniqueidentifier)
                RETURNS TABLE
                AS
                RETURN
                    SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0) AS Balance
                    FROM dbo.LedgerEntries
                    WHERE AccountId = @AccountId;
                """);

            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_RefreshBalanceSnapshots;");

            migrationBuilder.Sql("""
                ALTER PROCEDURE dbo.usp_PostLedgerTransaction
                    @TransactionId uniqueidentifier,
                    @Type          tinyint,
                    @ReferenceId   nvarchar(64),
                    @Description   nvarchar(256),
                    @CreatedAtUtc  datetime2(3),
                    @Entries       dbo.LedgerEntryList READONLY,
                    @EventId       uniqueidentifier,
                    @EventType     nvarchar(256),
                    @EventPayload  nvarchar(max)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    BEGIN TRANSACTION;

                    INSERT INTO dbo.LedgerTransactions (Id, Type, ReferenceId, Description, CreatedAtUtc)
                    VALUES (@TransactionId, @Type, @ReferenceId, @Description, @CreatedAtUtc);

                    INSERT INTO dbo.LedgerEntries (TransactionId, AccountId, Direction, Amount, CreatedAtUtc)
                    SELECT @TransactionId, e.AccountId, e.Direction, e.Amount, @CreatedAtUtc
                    FROM @Entries AS e;

                    INSERT INTO dbo.OutboxMessages (Id, Type, Payload, OccurredAtUtc)
                    VALUES (@EventId, @EventType, @EventPayload, @CreatedAtUtc);

                    COMMIT TRANSACTION;
                END
                """);

            migrationBuilder.DropTable(
                name: "AccountBalanceSnapshots");

            migrationBuilder.DropTable(
                name: "BalanceSnapshotRuns");

            // READ_COMMITTED_SNAPSHOT stays on: it is Azure SQL's default, and the rest of the
            // schema is correct with or without it (ADR 0002).
        }
    }
}
