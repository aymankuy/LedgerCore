using FluentValidation;

namespace LedgerCore.Application.Reports;

/// <summary>A report's [FromUtc, ToUtc) window and optional currency filter.</summary>
public interface IReportWindow
{
    DateTime FromUtc { get; }

    DateTime ToUtc { get; }

    string? Currency { get; }
}

/// <summary>Shared by every report validator through <c>Include</c>.</summary>
internal sealed class ReportWindowValidator : AbstractValidator<IReportWindow>
{
    public const int MaxWindowDays = 366;

    public ReportWindowValidator()
    {
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
            .WithMessage($"A report may cover at most {MaxWindowDays} days.");
        RuleFor(q => q.Currency)
            .Matches("^[A-Z]{3}$")
            .When(q => q.Currency is not null)
            .WithMessage("Currency must be a three-letter ISO 4217 code, e.g. EUR.");
    }
}
