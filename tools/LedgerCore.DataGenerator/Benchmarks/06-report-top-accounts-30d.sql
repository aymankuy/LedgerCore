-- GET /api/reports/top-accounts: top 10 EUR customer accounts, the same 30-day window.
EXEC dbo.usp_GetTopAccountsByVolume @FromUtc = @FromUtc, @ToUtc = @ToUtc, @Currency = 'EUR', @Top = 10;
