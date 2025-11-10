using FluentValidation;

namespace A2I.WebAPI.Validators;

public static class CommonValidationRules
{
    public static IRuleBuilder<T, string> Password<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Password cannot be empty")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number")
            .Matches(@"[@$!%*?&#]").WithMessage("Password must contain at least one special character");
    }

    public static IRuleBuilderOptions<T, string?> Email<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Email cannot be empty")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters");
    }

    public static IRuleBuilder<T, string> Username<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("Username cannot be empty")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters long")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters")
            .Matches(@"^[a-zA-Z0-9_-]+$").WithMessage("Username can only contain letters, numbers, underscore and hyphen");
    }

    public static IRuleBuilderOptions<T, string?> PhoneNumber<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Matches(@"^(\+84|0)[0-9]{9,10}$").WithMessage("Invalid phone number format")
            .When(x => !string.IsNullOrEmpty(ruleBuilder.ToString()));
    }
}