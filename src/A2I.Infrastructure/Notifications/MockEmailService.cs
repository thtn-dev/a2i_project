using A2I.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.Notifications;

public class MockEmailService : IEmailService
{
    private readonly ILogger<MockEmailService> _logger;
    
    public MockEmailService(ILogger<MockEmailService> logger)
    {
        _logger = logger;
    }
    
    public Task SendWelcomeEmailAsync(Guid customerId, string email, string planName, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "📧 [MOCK EMAIL] Welcome to {PlanName}! Sent to customer {CustomerId} ({Email})",
            planName, customerId, email);
        return Task.CompletedTask;
    }
    
    public Task SendReceiptEmailAsync(Guid customerId, Guid invoiceId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "📧 [MOCK EMAIL] Payment receipt for invoice {InvoiceId} sent to customer {CustomerId}",
            invoiceId, customerId);
        return Task.CompletedTask;
    }
    
    public Task SendPaymentFailedEmailAsync(
        Guid customerId, Guid invoiceId, int attemptCount, DateTime? nextRetry, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "📧 [MOCK EMAIL] Payment failed (attempt {Attempt}) for invoice {InvoiceId}. Customer {CustomerId}. Next retry: {NextRetry}",
            attemptCount, invoiceId, customerId, nextRetry);
        return Task.CompletedTask;
    }
    
    public Task SendCancellationEmailAsync(
        Guid customerId, string planName, DateTime endDate, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "📧 [MOCK EMAIL] Subscription cancellation confirmed for customer {CustomerId}. Plan: {PlanName}, Ends: {EndDate}",
            customerId, planName, endDate);
        return Task.CompletedTask;
    }
    
    public Task SendTrialEndingEmailAsync(
        Guid customerId, string planName, DateTime trialEndDate, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "📧 [MOCK EMAIL] Trial ending soon! Customer {CustomerId}, Plan: {PlanName}, Ends: {TrialEndDate}",
            customerId, planName, trialEndDate);
        return Task.CompletedTask;
    }
    
    public Task SendPaymentActionRequiredEmailAsync(
        Guid customerId, Guid invoiceId, string actionUrl, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "📧 [MOCK EMAIL] Payment action required (3D Secure) for invoice {InvoiceId}. Customer {CustomerId}. Action URL: {ActionUrl}",
            invoiceId, customerId, actionUrl);
        return Task.CompletedTask;
    }
}