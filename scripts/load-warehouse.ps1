# Loads the warehouse from the ledger on a SQL Server you can reach, the way the SSIS package and
# the Azure Data Factory pipeline do: take the safe window from the ledger, copy it to staging,
# call the load procedures in data/warehouse/003_etl_procedures.sql. Creates the warehouse
# database from data/warehouse/*.sql on the first run. Safe to run again: only new rows load.
#
#   powershell -File scripts/load-warehouse.ps1            # the docker compose database
#   powershell -File scripts/load-warehouse.ps1 -Server "host,1433" -User sa -Password "..."
#
# Windows PowerShell 5.1 (System.Data.SqlClient is built in).
param(
    [string]$Server = "127.0.0.1,14333",
    [string]$User = "sa",
    [string]$Password = "LocalOnly_Sa_9x2!",
    [string]$LedgerDatabase = "LedgerCore",
    [string]$WarehouseDatabase = "LedgerCoreDW"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function New-Connection([string]$database) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=$database;User Id=$User;Password=$Password;TrustServerCertificate=True"
    $connection.Open()
    return $connection
}

function Invoke-Sql([System.Data.SqlClient.SqlConnection]$connection, [string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 0
    return $command.ExecuteScalar()
}

# 1. The warehouse database and its schema, once.
$master = New-Connection "master"
Invoke-Sql $master "IF DB_ID(N'$WarehouseDatabase') IS NULL CREATE DATABASE [$WarehouseDatabase];" | Out-Null
$master.Close()

$warehouse = New-Connection $WarehouseDatabase
if ((Invoke-Sql $warehouse "SELECT OBJECT_ID(N'dbo.FactLedgerEntry')") -is [DBNull]) {
    foreach ($file in Get-ChildItem (Join-Path $root "data/warehouse/*.sql") | Sort-Object Name) {
        foreach ($batch in [regex]::Split((Get-Content $file.FullName -Raw), '(?im)^\s*GO\s*$')) {
            if ($batch.Trim()) { Invoke-Sql $warehouse $batch | Out-Null }
        }
        Write-Host "Applied $($file.Name)"
    }
}

# 2. Extract: the safe window of entries, and the whole status history.
$ledger = New-Connection $LedgerDatabase
Invoke-Sql $warehouse "EXEC etl.usp_ClearStaging" | Out-Null
$from = Invoke-Sql $warehouse "SELECT LastValue FROM etl.Watermark WHERE Source = N'LedgerEntries'"
$to = Invoke-Sql $ledger "EXEC dbo.usp_GetSafeEntryWatermark"
Write-Host "Entries after $from up to $to"

function Copy-ToStaging([string]$sql, [string]$table) {
    $command = $ledger.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 0
    $reader = $command.ExecuteReader()
    $bulk = New-Object System.Data.SqlClient.SqlBulkCopy $warehouse
    $bulk.DestinationTableName = $table
    $bulk.BulkCopyTimeout = 0
    $bulk.WriteToServer($reader)
    $reader.Close()
}

Copy-ToStaging "EXEC dbo.usp_ExtractLedgerEntries @FromEntryId = $from, @ToEntryId = $to" "stg.LedgerEntry"
Copy-ToStaging "EXEC dbo.usp_ExtractAccountStatusHistory" "stg.AccountStatusChange"

# 3. Load: the dimension first, then the facts.
Invoke-Sql $warehouse "EXEC etl.usp_LoadDimAccount" | Out-Null
Invoke-Sql $warehouse "EXEC etl.usp_LoadFactLedgerEntry" | Out-Null

$audit = $warehouse.CreateCommand()
$audit.CommandText = "SELECT TOP (2) TargetTable, RowsRead, RowsLoaded, RowsRejected FROM etl.LoadAudit ORDER BY Id DESC"
$reader = $audit.ExecuteReader()
while ($reader.Read()) {
    Write-Host ("{0}: read {1}, loaded {2}, rejected {3}" -f $reader[0], $reader[1], $reader[2], $reader[3])
}
$reader.Close()
$ledger.Close()
$warehouse.Close()
