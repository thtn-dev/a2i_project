namespace A2I.Application.StripeAbstraction.Portal;

public interface IStripePortalService
{
    Task<PortalSessionView> CreatePortalSessionAsync(string customerId, string returnUrl,
        CancellationToken ct = default);
}

public sealed class PortalSessionView
{
    public required string Id { get; set; } // bps_*
    public string? Url { get; set; }
}