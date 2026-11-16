using LedgerCore.Application.Reports;

namespace LedgerCore.UnitTests.Application;

public class ReportQueryValidatorTests
{
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("EUR")]
    public void Accepts_a_thirty_day_window_with_or_without_a_currency(string? currency)
    {
        new GetDailyVolumeQueryValidator().Validate(new GetDailyVolumeQuery(From, From.AddDays(30), currency))
            .IsValid.Should().BeTrue();
        new GetTopAccountsQueryValidator().Validate(new GetTopAccountsQuery(From, From.AddDays(30), currency, 10))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, "EUR")]
    [InlineData(367, "EUR")]
    [InlineData(30, "eur")]
    [InlineData(30, "EURO")]
    public void Rejects_a_bad_window_or_currency(int days, string currency)
    {
        new GetDailyVolumeQueryValidator().Validate(new GetDailyVolumeQuery(From, From.AddDays(days), currency))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_local_time()
    {
        var local = DateTime.SpecifyKind(From, DateTimeKind.Local);

        new GetDailyVolumeQueryValidator().Validate(new GetDailyVolumeQuery(local, From.AddDays(1), null))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GetTopAccountsQueryValidator.MaxTop + 1)]
    public void Rejects_a_top_outside_one_to_the_maximum(int top)
    {
        new GetTopAccountsQueryValidator().Validate(new GetTopAccountsQuery(From, From.AddDays(30), null, top))
            .IsValid.Should().BeFalse();
    }
}
