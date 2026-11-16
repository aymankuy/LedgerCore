using LedgerCore.Application.Accounts.GetStatement;

namespace LedgerCore.UnitTests.Application;

public class GetAccountStatementQueryValidatorTests
{
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly GetAccountStatementQueryValidator _validator = new();

    [Fact]
    public void Accepts_a_thirty_day_window()
    {
        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, From.AddDays(30)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_window_that_ends_before_it_starts()
    {
        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, From.AddDays(-1)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_window_longer_than_the_maximum()
    {
        var to = From.AddDays(GetAccountStatementQueryValidator.MaxWindowDays + 1);

        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, to));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_non_utc_timestamps()
    {
        var local = DateTime.SpecifyKind(From, DateTimeKind.Local);

        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), local, From.AddDays(1)));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GetAccountStatementQuery.MaxPageSize + 1)]
    public void Rejects_a_page_size_out_of_range(int pageSize)
    {
        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, From.AddDays(1), pageSize));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("")]
    [InlineData("MTIzOmFiYw")] // "123:abc"
    public void Rejects_a_cursor_it_did_not_issue(string cursor)
    {
        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, From.AddDays(1), Cursor: cursor));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_a_cursor_from_a_previous_page()
    {
        var cursor = new StatementCursor(From.AddHours(1), 42).Encode();

        var result = _validator.Validate(new GetAccountStatementQuery(Guid.NewGuid(), From, From.AddDays(1), Cursor: cursor));

        result.IsValid.Should().BeTrue();
    }
}

public class StatementCursorTests
{
    [Fact]
    public void Round_trips_the_position_to_the_millisecond()
    {
        var at = new DateTime(2026, 3, 1, 9, 30, 15, 123, DateTimeKind.Utc);

        StatementCursor.TryDecode(new StatementCursor(at, 9_876_543_210).Encode(), out var decoded).Should().BeTrue();

        decoded.Should().Be(new StatementCursor(at, 9_876_543_210));
        decoded.AfterUtc.Kind.Should().Be(DateTimeKind.Utc);
    }
}
