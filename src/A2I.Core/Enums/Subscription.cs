namespace A2I.Core.Enums;

public enum SubscriptionStatus
{
    Incomplete = 0,
    IncompleteExpired = 1,
    Trialing = 2,
    Active = 3,
    PastDue = 4,
    Canceled = 5,
    Unpaid = 6,
    Paused = 7
}

public enum InvoiceStatus
{
    Draft = 0,
    Open = 1,
    Paid = 2,
    Uncollectible = 3,
    Void = 4
}

public enum BillingInterval
{
    Month = 30,
    Year = 365
}