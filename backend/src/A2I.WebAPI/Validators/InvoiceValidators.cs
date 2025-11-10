using A2I.Application.Invoices;
using FluentValidation;

namespace A2I.WebAPI.Validators;

// Get Invoices Request Validator
public sealed class GetInvoicesRequestValidator : AbstractValidator<GetInvoicesRequest>
{
    private static readonly string[] ValidStatuses =
    [
        "draft", 
        "open", 
        "paid", 
        "void", 
        "uncollectible"
    ];

    public GetInvoicesRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be greater than or equal to 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100");

        RuleFor(x => x.Status)
            .Must(status => ValidStatuses.Contains(status!.ToLower()))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}")
            .When(x => !string.IsNullOrWhiteSpace(x.Status));

        RuleFor(x => x.FromDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("From date cannot be in the future")
            .When(x => x.FromDate.HasValue);

        RuleFor(x => x.ToDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("To date cannot be in the future")
            .When(x => x.ToDate.HasValue);

        RuleFor(x => x)
            .Must(x => x.FromDate <= x.ToDate)
            .WithMessage("From date must be less than or equal to To date")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);

        RuleFor(x => x)
            .Must(x => (x.ToDate!.Value - x.FromDate!.Value).TotalDays <= 365)
            .WithMessage("Date range cannot exceed 365 days")
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}