-- The load logic lives here, in T-SQL, not inside SSIS or Azure Data Factory. Both tools only
-- copy OLTP rows into the stg tables and call these procedures, so the two pipelines cannot
-- drift apart and the logic can be tested against a real SQL Server (see WarehouseLoadTests).
--
-- A load is: etl.usp_ClearStaging, copy, etl.usp_LoadDimAccount, etl.usp_LoadFactLedgerEntry.
-- The dimension goes first because a fact row points at the account version that was valid
-- when the entry was posted.
--
-- Re-running a load changes nothing: each procedure skips what it has already loaded.

CREATE PROCEDURE etl.usp_ClearStaging
AS
BEGIN
    SET NOCOUNT ON;
    TRUNCATE TABLE stg.LedgerEntry;
    TRUNCATE TABLE stg.AccountStatusChange;
END
GO

-- Slowly changing dimension type 2. Every staged status change that is not yet a version of
-- the account becomes a new version; the version that was open before it is closed at the
-- moment of the change. The staging copy is the whole of AccountStatusHistory, which is as
-- small as the number of accounts, so no watermark is needed here: SourceHistoryId says which
-- rows are already loaded. (A history id is allocated before its transaction commits, so a
-- watermark on it could skip a late commit. For one account, changes are serialised by the row
-- lock on Accounts, so versions of an account always arrive in order.)
CREATE PROCEDURE etl.usp_LoadDimAccount
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StartedAtUtc datetime2(3) = SYSUTCDATETIME(),
            @Read int = (SELECT COUNT(*) FROM stg.AccountStatusChange),
            @Loaded int;

    BEGIN TRANSACTION;

    SELECT s.HistoryId, s.AccountId, s.OwnerUserId, s.Currency, s.Kind, s.AllowsOverdraft, s.Status, s.ChangedAtUtc,
           LEAD(s.ChangedAtUtc) OVER (PARTITION BY s.AccountId ORDER BY s.HistoryId) AS NextChangedAtUtc
    INTO #New
    FROM stg.AccountStatusChange AS s
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DimAccount AS d WHERE d.SourceHistoryId = s.HistoryId);

    -- Close the open version of every account that has a newer status.
    UPDATE d
    SET ValidToUtc = f.FirstChangedAtUtc, IsCurrent = 0
    FROM dbo.DimAccount AS d
    JOIN (SELECT AccountId, MIN(ChangedAtUtc) AS FirstChangedAtUtc FROM #New GROUP BY AccountId) AS f
        ON f.AccountId = d.AccountId
    WHERE d.IsCurrent = 1;

    -- Several new versions of one account chain together; the last one stays open.
    INSERT INTO dbo.DimAccount
        (AccountId, OwnerUserId, Currency, Kind, KindName, Status, StatusName, AllowsOverdraft,
         ValidFromUtc, ValidToUtc, IsCurrent, SourceHistoryId)
    SELECT n.AccountId, n.OwnerUserId, n.Currency, n.Kind,
           CASE n.Kind WHEN 1 THEN N'Customer' WHEN 2 THEN N'Settlement' WHEN 3 THEN N'FeeRevenue' ELSE N'Unknown' END,
           n.Status,
           CASE n.Status WHEN 1 THEN N'Active' WHEN 2 THEN N'Frozen' WHEN 3 THEN N'Closed' ELSE N'Unknown' END,
           n.AllowsOverdraft,
           n.ChangedAtUtc,
           COALESCE(n.NextChangedAtUtc, '9999-12-31'),
           CASE WHEN n.NextChangedAtUtc IS NULL THEN 1 ELSE 0 END,
           n.HistoryId
    FROM #New AS n
    ORDER BY n.HistoryId;
    SET @Loaded = @@ROWCOUNT;

    INSERT INTO etl.LoadAudit (TargetTable, StartedAtUtc, FinishedAtUtc, RowsRead, RowsLoaded, RowsRejected)
    VALUES (N'DimAccount', @StartedAtUtc, SYSUTCDATETIME(), @Read, @Loaded, 0);

    COMMIT TRANSACTION;
END
GO

-- Appends staged entries after the watermark. A row that cannot be loaded (no account version
-- covers its timestamp, or an unknown transaction type) goes to etl.RejectedRow with a reason
-- and the load carries on. The watermark moves in the same transaction as the rows it covers.
CREATE PROCEDURE etl.usp_LoadFactLedgerEntry
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StartedAtUtc datetime2(3) = SYSUTCDATETIME(),
            @Watermark bigint,
            @Read int,
            @Rejected int,
            @Loaded int,
            @FromDate date,
            @ToDate date;

    BEGIN TRANSACTION;

    SELECT @Watermark = LastValue FROM etl.Watermark WITH (UPDLOCK) WHERE Source = N'LedgerEntries';

    -- The version of the account that was valid at the moment the entry was posted.
    SELECT s.EntryId, s.TransactionId, s.TransactionType, s.Direction, s.Amount, s.CreatedAtUtc, d.AccountKey,
           CASE WHEN t.TransactionTypeKey IS NULL THEN 1 ELSE 0 END AS UnknownType
    INTO #Batch
    FROM stg.LedgerEntry AS s
    LEFT JOIN dbo.DimAccount AS d
        ON d.AccountId = s.AccountId AND s.CreatedAtUtc >= d.ValidFromUtc AND s.CreatedAtUtc < d.ValidToUtc
    LEFT JOIN dbo.DimTransactionType AS t ON t.TransactionTypeKey = s.TransactionType
    WHERE s.EntryId > @Watermark;
    SET @Read = @@ROWCOUNT;

    INSERT INTO etl.RejectedRow (SourceTable, SourceKey, Reason, RejectedAtUtc)
    SELECT N'LedgerEntry', CONVERT(nvarchar(64), b.EntryId),
           CASE WHEN b.AccountKey IS NULL THEN N'No account version was valid when the entry was posted.'
                ELSE N'Unknown transaction type.' END,
           SYSUTCDATETIME()
    FROM #Batch AS b
    WHERE b.AccountKey IS NULL OR b.UnknownType = 1;
    SET @Rejected = @@ROWCOUNT;

    SELECT @FromDate = CONVERT(date, MIN(CreatedAtUtc)), @ToDate = CONVERT(date, MAX(CreatedAtUtc)) FROM #Batch;
    IF @FromDate IS NOT NULL
        EXEC dbo.usp_PopulateDimDate @From = @FromDate, @To = @ToDate;

    -- The primary key on EntryId is the last line of defence against a row loaded twice.
    INSERT INTO dbo.FactLedgerEntry
        (EntryId, TransactionId, DateKey, AccountKey, TransactionTypeKey, Direction, Amount, SignedAmount, CreatedAtUtc)
    SELECT b.EntryId, b.TransactionId,
           YEAR(b.CreatedAtUtc) * 10000 + MONTH(b.CreatedAtUtc) * 100 + DAY(b.CreatedAtUtc),
           b.AccountKey, b.TransactionType, b.Direction, b.Amount,
           CASE b.Direction WHEN 2 THEN b.Amount ELSE -b.Amount END,
           b.CreatedAtUtc
    FROM #Batch AS b
    WHERE b.AccountKey IS NOT NULL AND b.UnknownType = 0
      AND NOT EXISTS (SELECT 1 FROM dbo.FactLedgerEntry AS f WHERE f.EntryId = b.EntryId)
    ORDER BY b.EntryId;
    SET @Loaded = @@ROWCOUNT;

    IF @Read > 0
        UPDATE etl.Watermark
        SET LastValue = (SELECT MAX(EntryId) FROM #Batch), UpdatedAtUtc = SYSUTCDATETIME()
        WHERE Source = N'LedgerEntries';

    INSERT INTO etl.LoadAudit (TargetTable, StartedAtUtc, FinishedAtUtc, RowsRead, RowsLoaded, RowsRejected)
    VALUES (N'FactLedgerEntry', @StartedAtUtc, SYSUTCDATETIME(), @Read, @Loaded, @Rejected);

    COMMIT TRANSACTION;
END
GO
