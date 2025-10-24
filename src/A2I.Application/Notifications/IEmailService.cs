namespace A2I.Application.Notifications;

public interface IEmailService
{
    Task SendWelcomeEmailAsync(Guid customerId, string email, string planName, CancellationToken ct = default);
    Task SendReceiptEmailAsync(Guid customerId, Guid invoiceId, CancellationToken ct = default);
    Task SendPaymentFailedEmailAsync(Guid customerId, Guid invoiceId, int attemptCount, DateTime? nextRetry, CancellationToken ct = default);
    Task SendCancellationEmailAsync(Guid customerId, string planName, DateTime endDate, CancellationToken ct = default);
    Task SendTrialEndingEmailAsync(Guid customerId, string planName, DateTime trialEndDate, CancellationToken ct = default);
    Task SendPaymentActionRequiredEmailAsync(Guid customerId, Guid invoiceId, string actionUrl, CancellationToken ct = default);
}
