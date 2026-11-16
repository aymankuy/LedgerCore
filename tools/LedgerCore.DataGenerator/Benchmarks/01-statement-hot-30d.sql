-- GET /api/accounts/{id}/statement: a hot customer account, 30-day window, first page at the
-- maximum page size (1,000 lines; the API default is 100).
-- GetAccountStatementQueryHandler calls this procedure.
EXEC dbo.usp_GetAccountStatement @AccountId = @AccountId, @FromUtc = @FromUtc, @ToUtc = @ToUtc, @PageSize = 1000;
