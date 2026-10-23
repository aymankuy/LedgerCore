using FluentValidation;

namespace LedgerCore.Application.Accounts.OpenAccount;

public sealed class OpenAccountCommandValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountCommandValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEmpty();
        RuleFor(c => c.Currency)
            .NotEmpty()
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be a three-letter ISO 4217 code, e.g. EUR.");
    }
}
