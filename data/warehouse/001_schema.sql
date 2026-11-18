-- LedgerCore warehouse: a star schema in its own database, separate from the OLTP ledger.
-- Apply to an empty database (local: LedgerCoreDW; Azure: a second Azure SQL database).
--
--   dbo  the star: FactLedgerEntry and its dimensions. This is all Power BI reads.
--   stg  staging tables. SSIS or Azure Data Factory copies OLTP rows here; the load
--        procedures in etl read them. Truncated at the start of every load.
--   etl  load control: the watermark, a row-count audit and the rows that failed to load.

CREATE SCHEMA stg;
GO
CREATE SCHEMA etl;
GO

-- Calendar dimension: one row per day, keyed yyyymmdd. Filled by dbo.usp_PopulateDimDate.
CREATE TABLE dbo.DimDate
(
    DateKey     int          NOT NULL CONSTRAINT PK_DimDate PRIMARY KEY,
    [Date]      date         NOT NULL CONSTRAINT UQ_DimDate_Date UNIQUE,
    [Year]      smallint     NOT NULL,
    [Quarter]   tinyint      NOT NULL,
    [Month]     tinyint      NOT NULL,
    MonthName   nvarchar(10) NOT NULL,
    [Day]       tinyint      NOT NULL,
    IsoWeekday  tinyint      NOT NULL, -- 1 = Monday ... 7 = Sunday, independent of @@DATEFIRST
    DayName     nvarchar(10) NOT NULL,
    IsWeekend   bit          NOT NULL
);
GO

-- Transaction types are a closed list in the ledger, so the key is the source value itself.
CREATE TABLE dbo.DimTransactionType
(
    TransactionTypeKey tinyint      NOT NULL CONSTRAINT PK_DimTransactionType PRIMARY KEY,
    Name               nvarchar(20) NOT NULL
);
GO

-- Account dimension, slowly changing type 2 on Status: every status an account has had is a
-- row with the interval it was valid. A fact row points at the version that was valid when the
-- entry was posted, so history stays correct after an account is frozen or closed.
-- The other attributes never change after an account is opened.
CREATE TABLE dbo.DimAccount
(
    AccountKey      int              NOT NULL IDENTITY(1, 1) CONSTRAINT PK_DimAccount PRIMARY KEY,
    AccountId       uniqueidentifier NOT NULL, -- business key from the OLTP Accounts table
    OwnerUserId     uniqueidentifier NOT NULL,
    Currency        char(3)          NOT NULL,
    Kind            tinyint          NOT NULL,
    KindName        nvarchar(20)     NOT NULL,
    Status          tinyint          NOT NULL,
    StatusName      nvarchar(20)     NOT NULL,
    AllowsOverdraft bit              NOT NULL,
    ValidFromUtc    datetime2(3)     NOT NULL,
    ValidToUtc      datetime2(3)     NOT NULL CONSTRAINT DF_DimAccount_ValidTo DEFAULT '9999-12-31', -- open-ended
    IsCurrent       bit              NOT NULL,
    SourceHistoryId bigint           NOT NULL, -- the OLTP AccountStatusHistory row this version came from
    CONSTRAINT UQ_DimAccount_SourceHistoryId UNIQUE (SourceHistoryId)
);
GO
-- At most one open version per account.
CREATE UNIQUE INDEX UX_DimAccount_Current ON dbo.DimAccount (AccountId) WHERE IsCurrent = 1;
-- The point-in-time lookup a fact load does for every entry.
CREATE INDEX IX_DimAccount_AccountId_ValidFrom ON dbo.DimAccount (AccountId, ValidFromUtc) INCLUDE (AccountKey, ValidToUtc);
GO

-- One row per ledger entry. EntryId is the OLTP LedgerEntries.Id, so a row can never be
-- loaded twice: the primary key refuses it.
CREATE TABLE dbo.FactLedgerEntry
(
    EntryId            bigint           NOT NULL CONSTRAINT PK_FactLedgerEntry PRIMARY KEY,
    TransactionId      uniqueidentifier NOT NULL,
    DateKey            int              NOT NULL CONSTRAINT FK_FactLedgerEntry_DimDate REFERENCES dbo.DimDate (DateKey),
    AccountKey         int              NOT NULL CONSTRAINT FK_FactLedgerEntry_DimAccount REFERENCES dbo.DimAccount (AccountKey),
    TransactionTypeKey tinyint          NOT NULL CONSTRAINT FK_FactLedgerEntry_DimTransactionType REFERENCES dbo.DimTransactionType (TransactionTypeKey),
    Direction          tinyint          NOT NULL, -- 1 = debit, 2 = credit
    Amount             decimal(19,4)    NOT NULL,
    SignedAmount       decimal(19,4)    NOT NULL, -- credit positive, debit negative: SUM per account is its balance
    CreatedAtUtc       datetime2(3)     NOT NULL
);
GO
-- Daily volume and fee revenue read by date and type; the balance report reads by account.
CREATE INDEX IX_FactLedgerEntry_DateKey ON dbo.FactLedgerEntry (DateKey, TransactionTypeKey) INCLUDE (AccountKey, Amount, SignedAmount);
CREATE INDEX IX_FactLedgerEntry_AccountKey ON dbo.FactLedgerEntry (AccountKey) INCLUDE (Amount, SignedAmount, DateKey);
GO

-- Staging: loaded by SSIS / Azure Data Factory, never read by reports.
CREATE TABLE stg.LedgerEntry
(
    EntryId         bigint           NOT NULL,
    TransactionId   uniqueidentifier NOT NULL,
    AccountId       uniqueidentifier NOT NULL,
    TransactionType tinyint          NOT NULL,
    Direction       tinyint          NOT NULL,
    Amount          decimal(19,4)    NOT NULL,
    CreatedAtUtc    datetime2(3)     NOT NULL
);
GO
CREATE TABLE stg.AccountStatusChange
(
    HistoryId       bigint           NOT NULL,
    AccountId       uniqueidentifier NOT NULL,
    OwnerUserId     uniqueidentifier NOT NULL,
    Currency        char(3)          NOT NULL,
    Kind            tinyint          NOT NULL,
    AllowsOverdraft bit              NOT NULL,
    Status          tinyint          NOT NULL,
    ChangedAtUtc    datetime2(3)     NOT NULL
);
GO

-- The highest OLTP entry id loaded so far. The next load starts after it.
CREATE TABLE etl.Watermark
(
    Source       nvarchar(50) NOT NULL CONSTRAINT PK_Watermark PRIMARY KEY,
    LastValue    bigint       NOT NULL,
    UpdatedAtUtc datetime2(3) NOT NULL
);
GO
INSERT INTO etl.Watermark (Source, LastValue, UpdatedAtUtc) VALUES (N'LedgerEntries', 0, SYSUTCDATETIME());
GO

-- One row per table per load: what was read, loaded and rejected.
CREATE TABLE etl.LoadAudit
(
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_LoadAudit PRIMARY KEY,
    TargetTable   nvarchar(50)       NOT NULL,
    StartedAtUtc  datetime2(3)       NOT NULL,
    FinishedAtUtc datetime2(3)       NOT NULL,
    RowsRead      int                NOT NULL,
    RowsLoaded    int                NOT NULL,
    RowsRejected  int                NOT NULL
);
GO

-- Error routing: a staged row that cannot be loaded is kept here with the reason,
-- instead of failing the whole load.
CREATE TABLE etl.RejectedRow
(
    Id            bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_RejectedRow PRIMARY KEY,
    SourceTable   nvarchar(50)          NOT NULL,
    SourceKey     nvarchar(64)          NOT NULL,
    Reason        nvarchar(200)         NOT NULL,
    RejectedAtUtc datetime2(3)          NOT NULL
);
GO
