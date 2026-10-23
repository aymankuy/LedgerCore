using FluentValidation;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;

namespace LedgerCore.Application.Ledger.Deposit;

public sealed class DepositCommandValidator : AbstractValidator<DepositCommand>
{
    public DepositCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.Amount)
            .GreaterThan(0)
            .PrecisionScale(Money.Precision, Money.Scale, ignoreTrailingZeros: true);
        RuleFor(c => c.ReferenceId).MaximumLength(LedgerTransaction.ReferenceIdMaxLength);
        RuleFor(c => c.Description).MaximumLength(LedgerTransaction.DescriptionMaxLength);
    }
}
