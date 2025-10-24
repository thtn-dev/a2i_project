// src/A2I.Infrastructure/StripeServices/WebhookHandlers/CustomerDeletedHandler.cs

using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class CustomerDeletedHandler : WebhookEventHandlerBase
{
    public CustomerDeletedHandler(
        ApplicationDbContext db,
        ILogger<CustomerDeletedHandler> logger)
        : base(db, logger)
    {
    }
    
    public override string EventType => EventTypes.CustomerDeleted;
    
    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var customer = stripeEvent.Data.Object as Customer;
        if (customer == null)
        {
            return new WebhookHandlerResult(false, "Invalid customer data");
        }
        
        // Find customer in DB
        var dbCustomer = await Db.Customers
            .Include(c => c.Subscriptions)
            .FirstOrDefaultAsync(c => c.StripeCustomerId == customer.Id, ct);
        
        if (dbCustomer == null)
        {
            Logger.LogWarning(
                "Customer {StripeCustomerId} not found in DB",
                customer.Id);
            
            return new WebhookHandlerResult(
                true,
                "Customer not found in DB");
        }
        
        // Check if customer has active subscriptions
        var hasActiveSubscriptions = dbCustomer.Subscriptions
            .Any(s => !s.IsDeleted && s.IsActive);
        
        if (hasActiveSubscriptions)
        {
            Logger.LogWarning(
                "Customer {CustomerId} deleted in Stripe but has active subscriptions in DB. " +
                "Manual review required.",
                dbCustomer.Id);
            
            // Don't delete - flag for manual review
            return new WebhookHandlerResult(
                true,
                "Customer has active subscriptions - manual review needed");
        }
        
        // Soft delete customer (preserve data)
        dbCustomer.IsDeleted = true;
        dbCustomer.DeletedAt = DateTime.UtcNow;
        dbCustomer.StripeCustomerId = null; // Clear Stripe ID
        
        await Db.SaveChangesAsync(ct);
        
        Logger.LogInformation(
            "Customer {CustomerId} soft deleted after Stripe deletion",
            dbCustomer.Id);
        
        return new WebhookHandlerResult(
            true,
            $"Customer {dbCustomer.Id} soft deleted");
    }
}