namespace A2I.Application.StripeAbstraction;

public class StripeOptions
{
    public string SecretKey { get; set; }
    public string PublishableKey { get; set; }
    public string WebhookSecret { get; set; }
    public string ApiVersion { get; set; }
    public string SuccessUrl { get; set; }
    public string CancelUrl { get; set; }
    public string IdempotencyPrefix  { get; set; } =  "customer_";
}

