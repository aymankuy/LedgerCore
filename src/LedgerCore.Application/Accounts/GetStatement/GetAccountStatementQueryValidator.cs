using FluentValidation;

namespace LedgerCore.Application.Accounts.GetStatement;

public sealed class GetAccountStatementQueryValidator : AbstractValidator<GetAccountStatementQuery>
{
    /// <summary>Keeps a single statement bounded; longer histories are paged by window.</summary>
    public const int MaxWindowDays = 366;

    public GetAccountStatementQueryValidator()
    {
        RuleFor(q => q.AccountId).NotEmpty();
        RuleFor(q => q.FromUtc)
            .Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("FromUtc must be a UTC timestamp.");
        RuleFor(q => q.ToUtc)
            .Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("ToUtc must be a UTC timestamp.")
            .GreaterThan(q => q.FromUtc)
            .WithMessage("ToUtc must be after FromUtc.");
        RuleFor(q => q)
            .Must(q => q.ToUtc - q.FromUtc <= TimeSpan.FromDays(MaxWindowDays))
            .WithName("Window")
            .WithMessage($"A statement may cover at most {MaxWindowDays} days.");
    }
}
