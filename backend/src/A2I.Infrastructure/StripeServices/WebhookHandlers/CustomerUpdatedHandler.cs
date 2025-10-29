using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class CustomerUpdatedHandler : WebhookEventHandlerBase
{
    public CustomerUpdatedHandler(
        ApplicationDbContext db,
        ILogger<CustomerUpdatedHandler> logger)
        : base(db, logger)
    {
    }

    public override string EventType => EventTypes.CustomerUpdated;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var customer = stripeEvent.Data.Object as Customer;
        if (customer == null) return new WebhookHandlerResult(false, "Invalid customer data");

        var dbCustomer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == customer.Id, ct);

        if (dbCustomer == null)
        {
            Logger.LogWarning(
                "Customer {StripeCustomerId} not found in DB (might not be synced yet)",
                customer.Id);

            return new WebhookHandlerResult(
                true,
                "Customer not found in DB (no sync needed)");
        }

        // Sync basic fields if changed
        var changes = new List<string>();

        if (dbCustomer.Email != customer.Email && !string.IsNullOrWhiteSpace(customer.Email))
        {
            dbCustomer.Email = customer.Email;
            changes.Add("email");
        }

        if (dbCustomer.Phone != customer.Phone)
        {
            dbCustomer.Phone = customer.Phone;
            changes.Add("phone");
        }

        // Parse name if provided
        if (!string.IsNullOrWhiteSpace(customer.Name))
        {
            var nameParts = customer.Name.Split(' ', 2);
            var newFirstName = nameParts.Length > 0 ? nameParts[0] : null;
            var newLastName = nameParts.Length > 1 ? nameParts[1] : null;

            if (dbCustomer.FirstName != newFirstName)
            {
                dbCustomer.FirstName = newFirstName;
                changes.Add("first_name");
            }

            if (dbCustomer.LastName != newLastName)
            {
                dbCustomer.LastName = newLastName;
                changes.Add("last_name");
            }
        }

        if (changes.Count != 0)
        {
            await Db.SaveChangesAsync(ct);

            Logger.LogInformation(
                "Synced customer {CustomerId} from Stripe. Changes: {Changes}",
                dbCustomer.Id, string.Join(", ", changes));
        }
        else
        {
            Logger.LogDebug(
                "No changes to sync for customer {CustomerId}",
                dbCustomer.Id);
        }

        return new WebhookHandlerResult(
            true,
            changes.Count != 0
                ? $"Customer synced: {string.Join(", ", changes)}"
                : "No changes to sync");
    }
}