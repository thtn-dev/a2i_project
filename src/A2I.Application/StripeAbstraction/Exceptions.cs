using System.Net;

namespace A2I.Application.StripeAbstraction;
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
    public BusinessException(string message, Exception inner) : base(message, inner) { }
}
public class StripeServiceException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? StripeCode { get; }
    public string? StripeRequestId { get; }
    public string? StripeErrorType { get; }

    public StripeServiceException(string message, Exception inner, HttpStatusCode? statusCode = null,
        string? stripeCode = null, string? stripeRequestId = null, string? stripeErrorType = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        StripeCode = stripeCode;
        StripeRequestId = stripeRequestId;
        StripeErrorType = stripeErrorType;
    }
}

public sealed class StripeNotFoundException : StripeServiceException
{
    public StripeNotFoundException(string message, Exception inner, HttpStatusCode? statusCode = null,
        string? stripeCode = null, string? stripeRequestId = null, string? stripeErrorType = null)
        : base(message, inner, statusCode, stripeCode, stripeRequestId, stripeErrorType) { }
}

public static class StripeErrorMapper
{
    public static Exception Wrap(Stripe.StripeException ex, string? overrideMessage = null)
    {
        var status = (HttpStatusCode?)ex.HttpStatusCode;
        var msg = overrideMessage ?? ex.StripeError?.Message ?? ex.Message;

        // map 404 to NotFound specialization
        if ((int?)ex.HttpStatusCode == 404 || string.Equals(ex.StripeError?.Code, "resource_missing", StringComparison.OrdinalIgnoreCase))
        {
            return new StripeNotFoundException(
                msg, ex, status, ex.StripeError?.Code, ex.Source, ex.StripeError?.Type);
        }

        return new StripeServiceException(
            msg, ex, status, ex.StripeError?.Code, ex.Source, ex.StripeError?.Type);
    }
}