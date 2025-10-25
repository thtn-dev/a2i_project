namespace A2I.Application.Common;

// <summary>
/// Centralized error codes for consistent error handling across the API
/// Format: [DOMAIN]_[NUMBER]
/// </summary>
public static class ErrorCodes
{
    // ==================== CUSTOMER ERRORS (1xxx) ====================
    
    /// <summary>Customer not found in database</summary>
    public const string CUSTOMER_NOT_FOUND = "CUSTOMER_1001";
    
    /// <summary>Customer already exists with this identifier</summary>
    public const string CUSTOMER_ALREADY_EXISTS = "CUSTOMER_1002";
    
    /// <summary>Customer must have a Stripe customer ID to perform this action</summary>
    public const string STRIPE_CUSTOMER_REQUIRED = "CUSTOMER_1003";
    
    /// <summary>Customer does not have permission to access this resource</summary>
    public const string CUSTOMER_ACCESS_DENIED = "CUSTOMER_1004";

    // ==================== SUBSCRIPTION ERRORS (2xxx) ====================
    
    /// <summary>Subscription not found</summary>
    public const string SUBSCRIPTION_NOT_FOUND = "SUBSCRIPTION_2001";
    
    /// <summary>Customer already has an active subscription</summary>
    public const string SUBSCRIPTION_ALREADY_ACTIVE = "SUBSCRIPTION_2002";
    
    /// <summary>Cannot downgrade plan during trial period</summary>
    public const string SUBSCRIPTION_CANNOT_DOWNGRADE_TRIAL = "SUBSCRIPTION_2003";
    
    /// <summary>Subscription is not active</summary>
    public const string SUBSCRIPTION_NOT_ACTIVE = "SUBSCRIPTION_2004";
    
    /// <summary>Subscription is already canceled</summary>
    public const string SUBSCRIPTION_ALREADY_CANCELED = "SUBSCRIPTION_2005";
    
    /// <summary>Invalid subscription status for this operation</summary>
    public const string SUBSCRIPTION_INVALID_STATUS = "SUBSCRIPTION_2006";

    // ==================== INVOICE ERRORS (3xxx) ====================
    
    /// <summary>Invoice not found</summary>
    public const string INVOICE_NOT_FOUND = "INVOICE_3001";
    
    /// <summary>Customer does not have permission to access this invoice</summary>
    public const string INVOICE_ACCESS_DENIED = "INVOICE_3002";
    
    /// <summary>Invoice PDF is not available</summary>
    public const string INVOICE_PDF_NOT_AVAILABLE = "INVOICE_3003";
    
    /// <summary>Invoice is not paid</summary>
    public const string INVOICE_NOT_PAID = "INVOICE_3004";

    // ==================== PLAN ERRORS (4xxx) ====================
    
    /// <summary>Plan not found</summary>
    public const string PLAN_NOT_FOUND = "PLAN_4001";
    
    /// <summary>Plan is inactive and cannot be subscribed to</summary>
    public const string PLAN_INACTIVE = "PLAN_4002";
    
    /// <summary>Cannot switch to the same plan</summary>
    public const string PLAN_ALREADY_SUBSCRIBED = "PLAN_4003";

    // ==================== STRIPE ERRORS (5xxx) ====================
    
    /// <summary>Stripe API error occurred</summary>
    public const string STRIPE_API_ERROR = "STRIPE_5001";
    
    /// <summary>Stripe webhook signature validation failed</summary>
    public const string STRIPE_WEBHOOK_INVALID = "STRIPE_5002";
    
    /// <summary>Stripe resource not found</summary>
    public const string STRIPE_RESOURCE_NOT_FOUND = "STRIPE_5003";
    
    /// <summary>Stripe payment failed</summary>
    public const string STRIPE_PAYMENT_FAILED = "STRIPE_5004";
    
    /// <summary>Stripe checkout session invalid</summary>
    public const string STRIPE_CHECKOUT_INVALID = "STRIPE_5005";

    // ==================== VALIDATION ERRORS (9xxx) ====================
    
    /// <summary>Request validation failed</summary>
    public const string VALIDATION_FAILED = "VALIDATION_9001";
    
    /// <summary>Required field is missing</summary>
    public const string VALIDATION_REQUIRED = "VALIDATION_9002";
    
    /// <summary>Field format is invalid</summary>
    public const string VALIDATION_FORMAT = "VALIDATION_9003";
    
    /// <summary>Field value is out of valid range</summary>
    public const string VALIDATION_RANGE = "VALIDATION_9004";

    // ==================== GENERAL ERRORS (0xxx) ====================
    
    /// <summary>Internal server error</summary>
    public const string INTERNAL_ERROR = "ERROR_0001";
    
    /// <summary>Resource not found</summary>
    public const string NOT_FOUND = "ERROR_0002";
    
    /// <summary>Bad request</summary>
    public const string BAD_REQUEST = "ERROR_0003";
    
    /// <summary>Unauthorized access</summary>
    public const string UNAUTHORIZED = "ERROR_0004";
    
    /// <summary>Forbidden access</summary>
    public const string FORBIDDEN = "ERROR_0005";
}

