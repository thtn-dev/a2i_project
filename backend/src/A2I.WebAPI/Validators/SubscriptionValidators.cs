using A2I.Application.Subscriptions;
using FluentValidation;

namespace A2I.WebAPI.Validators;

public sealed class StartSubscriptionRequestValidator : AbstractValidator<StartSubscriptionRequest>
{
    public StartSubscriptionRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer ID cannot be empty");

        RuleFor(x => x.PlanId)
            .NotEmpty().WithMessage("Plan ID cannot be empty");

        RuleFor(x => x.SuccessUrl)
            .NotEmpty().WithMessage("Success URL cannot be empty")
            .Must(BeAValidUrl).WithMessage("Success URL must be a valid URL")
            .MaximumLength(2048).WithMessage("Success URL cannot exceed 2048 characters");

        RuleFor(x => x.CancelUrl)
            .NotEmpty().WithMessage("Cancel URL cannot be empty")
            .Must(BeAValidUrl).WithMessage("Cancel URL must be a valid URL")
            .MaximumLength(2048).WithMessage("Cancel URL cannot exceed 2048 characters");

        RuleFor(x => x)
            .Must(x => x.SuccessUrl != x.CancelUrl)
            .WithMessage("Success URL and Cancel URL must be different")
            .When(x => !string.IsNullOrWhiteSpace(x.SuccessUrl) && !string.IsNullOrWhiteSpace(x.CancelUrl));

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

    private static bool BeAValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

// Cancel Subscription Request Validator
public sealed class CancelSubscriptionRequestValidator : AbstractValidator<CancelSubscriptionRequest>
{
    public CancelSubscriptionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Cancellation reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}

// Upgrade Subscription Request Validator
public sealed class UpgradeSubscriptionRequestValidator : AbstractValidator<UpgradeSubscriptionRequest>
{
    public UpgradeSubscriptionRequestValidator()
    {
        RuleFor(x => x.NewPlanId)
            .NotEmpty().WithMessage("New plan ID cannot be empty");
    }
}