using FluentValidation;
using LedgerCore.Domain.Users;

namespace LedgerCore.Application.Auth.Register;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public RegisterUserCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(c => c.Password).NotEmpty().Length(PasswordMinLength, PasswordMaxLength);
    }
}
