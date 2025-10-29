namespace A2I.Application.StripeAbstraction.Customers;

// Requests
public sealed class CreateCustomerRequest
{
    public required string Email { get; set; }
    public string? Name { get; set; } // you can compose from First/Last in caller
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? PaymentMethodId { get; set; } // optional initial PM
    public bool? InvoiceSettingsDefaultToAutoCollection { get; set; } // optional
}

public sealed class UpdateCustomerRequest
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? DefaultPaymentMethodId { get; set; } // set default PM (invoice_settings.default_payment_method)
    public bool? Delinquent { get; set; } // rarely used; here for completeness
}

// Views (Responses)
public sealed class CustomerView
{
    public required string Id { get; set; } // Stripe Customer Id (cus_xxx)
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string? DefaultPaymentMethodId { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    public bool Deleted { get; set; }
}

public sealed class PaymentMethodView
{
    public required string Id { get; set; } // pm_xxx
    public string Type { get; set; } = "card";
    public string? Brand { get; set; } // visa/mastercard/...
    public string? Last4 { get; set; }
    public long? ExpMonth { get; set; }
    public long? ExpYear { get; set; }
    public bool IsDefaultForInvoices { get; set; }
}

public sealed class AttachPaymentMethodResult
{
    public required string CustomerId { get; set; }
    public required string PaymentMethodId { get; set; }
    public bool SetAsDefaultForInvoices { get; set; }
}