namespace A2I.Application.Customers;

/// <summary>
///     Orchestrates customer management between DB and Stripe
/// </summary>
public interface ICustomerApplicationService
{
    /// <summary>
    ///     Create or update customer in both DB and Stripe
    ///     Business Rules:
    ///     - Create Stripe customer if not exists
    ///     - Update existing Stripe customer if already exists
    ///     - Sync customer data between DB and Stripe
    /// </summary>
    Task<CustomerDetailsResponse> CreateOrUpdateCustomerAsync(
        CreateOrUpdateCustomerRequest request,
        CancellationToken ct = default);

    /// <summary>
    ///     Get customer details with subscription and invoices
    ///     Returns:
    ///     - Customer info
    ///     - Active subscription (if any)
    ///     - Recent invoices (last 5)
    ///     - Payment methods
    /// </summary>
    Task<CustomerDetailsResponse> GetCustomerDetailsAsync(
        Guid customerId,
        CancellationToken ct = default);

    /// <summary>
    ///     Update payment method for customer
    ///     Business Rules:
    ///     - Verify payment method ownership
    ///     - Attach to Stripe customer
    ///     - Update subscription if needed
    ///     - Set as default if requested
    /// </summary>
    Task<UpdatePaymentMethodResponse> UpdatePaymentMethodAsync(
        Guid customerId,
        UpdatePaymentMethodRequest request,
        CancellationToken ct = default);

    /// <summary>
    ///     Get Stripe Customer Portal URL for self-service management
    ///     Customer can:
    ///     - Update payment methods
    ///     - View invoices
    ///     - Cancel subscription
    ///     - Update billing info
    /// </summary>
    Task<CustomerPortalResponse> GetCustomerPortalUrlAsync(
        Guid customerId,
        string returnUrl,
        CancellationToken ct = default);
}