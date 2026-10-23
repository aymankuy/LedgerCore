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
}
