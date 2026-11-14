-- GET /api/accounts/{id}/statement: a hot customer account, 30-day window.
-- GetAccountStatementQueryHandler calls this procedure.
EXEC dbo.usp_GetAccountStatement @AccountId = @AccountId, @FromUtc = @FromUtc, @ToUtc = @ToUtc;
