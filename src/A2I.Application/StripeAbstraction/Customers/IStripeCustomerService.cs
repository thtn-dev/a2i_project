namespace A2I.Application.StripeAbstraction.Customers;

public interface IStripeCustomerService
{
    Task<CustomerView> CreateCustomerAsync(CreateCustomerRequest req, CancellationToken ct = default);
    Task<CustomerView?> GetCustomerAsync(string stripeCustomerId, CancellationToken ct = default);

    Task<CustomerView> UpdateCustomerAsync(string stripeCustomerId, UpdateCustomerRequest req,
        CancellationToken ct = default);

    Task<bool> DeleteCustomerAsync(string stripeCustomerId, CancellationToken ct = default);

    Task<AttachPaymentMethodResult> AttachPaymentMethodAsync(string customerId, string paymentMethodId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethodView>> ListPaymentMethodsAsync(string customerId, CancellationToken ct = default);
}