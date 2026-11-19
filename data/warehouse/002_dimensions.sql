INSERT INTO dbo.DimTransactionType (TransactionTypeKey, Name)
VALUES (1, N'Deposit'), (2, N'Withdrawal'), (3, N'Transfer'), (4, N'Fee'), (5, N'Reversal');
GO

-- Adds every day from @From to @To that is not in DimDate yet, so it is safe to call again.
CREATE PROCEDURE dbo.usp_PopulateDimDate
    @From date,
    @To   date
AS
BEGIN
    SET NOCOUNT ON;

    WITH Days AS
    (
        SELECT @From AS D
        UNION ALL
        SELECT DATEADD(day, 1, D) FROM Days WHERE D < @To
    )
    INSERT INTO dbo.DimDate (DateKey, [Date], [Year], [Quarter], [Month], MonthName, [Day], IsoWeekday, DayName, IsWeekend)
    SELECT YEAR(D) * 10000 + MONTH(D) * 100 + DAY(D),
           D,
           YEAR(D),
           DATEPART(quarter, D),
           MONTH(D),
           DATENAME(month, D),
           DAY(D),
           ((DATEPART(weekday, D) + @@DATEFIRST - 2) % 7) + 1,
           DATENAME(weekday, D),
           CASE WHEN ((DATEPART(weekday, D) + @@DATEFIRST - 2) % 7) + 1 >= 6 THEN 1 ELSE 0 END
    FROM Days
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DimDate AS x WHERE x.[Date] = Days.D)
    OPTION (MAXRECURSION 0);
END
GO

EXEC dbo.usp_PopulateDimDate @From = '2026-01-01', @To = '2030-12-31';
GO
