using A2I.Infrastructure.Identity.Models;
using FluentValidation;

namespace A2I.WebAPI.Validators;

// Register Request Validator
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username).Username();
        
        RuleFor(x => x.Email).Email();

        RuleFor(x => x.Password).Password();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password cannot be empty")
            .Equal(x => x.Password).WithMessage("Confirm password does not match the password");
    }
}

// Login Request Validator
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username cannot be empty");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password cannot be empty");
    }
}

// Refresh Token Request Validator
public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token cannot be empty");
    }
}

// Change Password Request Validator
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password cannot be empty");

        RuleFor(x => x.NewPassword).Password();

        RuleFor(x => x.ConfirmNewPassword)
            .NotEmpty().WithMessage("Confirm password cannot be empty")
            .Equal(x => x.NewPassword).WithMessage("Confirm password does not match the new password");

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password cannot be the same as the current password");
    }
}

// Forgot Password Request Validator
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).Email();
    }
}

// Reset Password Request Validator
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).Email();

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token cannot be empty");

        RuleFor(x => x.NewPassword).Password();

        RuleFor(x => x.ConfirmNewPassword)
            .NotEmpty().WithMessage("Confirm password cannot be empty")
            .Equal(x => x.NewPassword).WithMessage("Confirm password does not match the new password");
    }
}

// Confirm Email Request Validator
public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID cannot be empty");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token cannot be empty");
    }
}

// Resend Email Confirmation Request Validator
public sealed class ResendEmailConfirmationRequestValidator : AbstractValidator<ResendEmailConfirmationRequest>
{
    public ResendEmailConfirmationRequestValidator()
    {
        RuleFor(x => x.Email).Email();
    }
}

// Update Profile Request Validator
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Email)
            .Email();

        RuleFor(x => x.PhoneNumber)
            .PhoneNumber();
    }
}

// Verify 2FA Request Validator
public sealed class Verify2FaRequestValidator : AbstractValidator<Verify2FARequest>
{
    public Verify2FaRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code cannot be empty")
            .Length(6).WithMessage("Verification code must be 6 digits")
            .Matches(@"^[0-9]{6}$").WithMessage("Verification code must contain only digits");
    }
}
