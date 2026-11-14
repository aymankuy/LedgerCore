-- GET /api/reports/daily-volume: every currency, the same 30-day window.
EXEC dbo.usp_GetDailyVolume @FromUtc = @FromUtc, @ToUtc = @ToUtc;
