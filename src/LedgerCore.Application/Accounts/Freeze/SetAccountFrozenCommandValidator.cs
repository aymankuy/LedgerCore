using FluentValidation;

namespace LedgerCore.Application.Accounts.Freeze;

public sealed class SetAccountFrozenCommandValidator : AbstractValidator<SetAccountFrozenCommand>
{
    public SetAccountFrozenCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
    }
}
