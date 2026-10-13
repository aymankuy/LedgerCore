using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using static LedgerCore.UnitTests.Domain.TestAccounts;

namespace LedgerCore.UnitTests.Domain;

public class CurrencyAndUserTests
{
    [Theory]
    [InlineData("EUR")]
    [InlineData("TRY")]
    public void Valid_currency_codes_are_accepted(string code)
    {
        Currency.From(code).Code.Should().Be(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void Invalid_currency_codes_are_rejected(string? code)
    {
        var act = () => Currency.From(code);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Register_normalizes_the_email()
    {
        var user = User.Register("  Ata@Example.COM ", "hash", UserRole.User, Now);

        user.Email.Should().Be("ata@example.com");
        user.Role.Should().Be(UserRole.User);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Register_rejects_invalid_email(string email)
    {
        var act = () => User.Register(email, "hash", UserRole.User, Now);

        act.Should().Throw<DomainValidationException>();
    }
}
