using FluentValidation;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;

namespace LedgerCore.Application.Ledger.Transfer;

public sealed class TransferMoneyCommandValidator : AbstractValidator<TransferMoneyCommand>
{
    public TransferMoneyCommandValidator()
    {
        RuleFor(c => c.SourceAccountId).NotEmpty();
        RuleFor(c => c.DestinationAccountId)
            .NotEmpty()
            .NotEqual(c => c.SourceAccountId)
            .WithMessage("Source and destination accounts must differ.");
        RuleFor(c => c.Amount)
            .GreaterThan(0)
            .PrecisionScale(Money.Precision, Money.Scale, ignoreTrailingZeros: true);
        RuleFor(c => c.ReferenceId).MaximumLength(LedgerTransaction.ReferenceIdMaxLength);
        RuleFor(c => c.Description).MaximumLength(LedgerTransaction.DescriptionMaxLength);
    }
}
