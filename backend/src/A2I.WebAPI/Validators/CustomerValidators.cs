using A2I.Application.Customers;
using FluentValidation;

namespace A2I.WebAPI.Validators;

// Create Or Update Customer Request Validator
public sealed class CreateOrUpdateCustomerRequestValidator : AbstractValidator<CreateOrUpdateCustomerRequest>
{
    public CreateOrUpdateCustomerRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer ID cannot be empty");

        RuleFor(x => x.Email)
            .Email();

        RuleFor(x => x.FirstName)
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.FirstName));

        RuleFor(x => x.LastName)
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.LastName));

        RuleFor(x => x.Phone)
            .PhoneNumber();

        RuleFor(x => x.CompanyName)
            .MaximumLength(200).WithMessage("Company name cannot exceed 200 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.CompanyName));

        RuleFor(x => x.PaymentMethodId)
            .NotEmpty().WithMessage("Payment method ID cannot be empty")
            .When(x => !string.IsNullOrWhiteSpace(x.PaymentMethodId));

        RuleFor(x => x.Metadata)
            .Must(metadata => metadata == null || metadata.Count <= 50)
            .WithMessage("Metadata cannot contain more than 50 entries")
            .When(x => x.Metadata != null);

        RuleForEach(x => x.Metadata!.Keys)
            .MaximumLength(40).WithMessage("Metadata key cannot exceed 40 characters")
            .When(x => x.Metadata != null);

        RuleForEach(x => x.Metadata!.Values)
            .MaximumLength(500).WithMessage("Metadata value cannot exceed 500 characters")
            .When(x => x.Metadata != null);
    }
}

// Update Payment Method Request Validator
public sealed class UpdatePaymentMethodRequestValidator : AbstractValidator<UpdatePaymentMethodRequest>
{
    public UpdatePaymentMethodRequestValidator()
    {
        RuleFor(x => x.PaymentMethodId)
            .NotEmpty().WithMessage("Payment method ID cannot be empty")
            .Matches(@"^pm_[a-zA-Z0-9]+$").WithMessage("Invalid Stripe payment method ID format");
    }
}

public sealed class GetPortalUrlRequestValidator : AbstractValidator<GetPortalUrlRequest>
{
    public GetPortalUrlRequestValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .NotEmpty().WithMessage("ReturnUrl cannot be empty")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ReturnUrl must be a valid absolute URL");
    }
}