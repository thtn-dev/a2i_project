using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class CustomerCreatedHandler : WebhookEventHandlerBase
{
    public CustomerCreatedHandler(
        ApplicationDbContext db,
        ILogger<CustomerCreatedHandler> logger)
        : base(db, logger)
    {
    }

    public override string EventType => EventTypes.CustomerCreated;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var customer = stripeEvent.Data.Object as Customer;
        if (customer == null) return new WebhookHandlerResult(false, "Invalid customer data");

        // Check if customer already exists in DB
        var dbCustomer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == customer.Id, ct);

        if (dbCustomer != null)
        {
            Logger.LogInformation(
                "Customer {StripeCustomerId} already exists in DB as {CustomerId}",
                customer.Id, dbCustomer.Id);

            return new WebhookHandlerResult(
                true,
                $"Customer already synced: {dbCustomer.Id}");
        }

        Logger.LogInformation(
            "Stripe customer created: {StripeCustomerId} ({Email}). " +
            "Note: DB customer should be created via application flow.",
            customer.Id, customer.Email);

        return new WebhookHandlerResult(
            true,
            $"Customer creation logged: {customer.Id}");
    }
}