using FluentResults;

namespace A2I.WebAPI.Extensions;

public static class ResultPatternExtensions
{
    /// <summary>
    /// Gets the first success message or returns the default message if no successes exist
    /// </summary>
    public static string GetSuccessMessage(this ResultBase result, string defaultMessage)
    {
        return result.Successes.Count > 0 
            ? result.Successes[0].Message 
            : defaultMessage;
    }
    
    /// <summary>
    /// Gets the first success message or returns empty string if no successes exist
    /// </summary>
    public static string GetSuccessMessage(this ResultBase result)
    {
        return result.Successes.Count > 0 
            ? result.Successes[0].Message 
            : string.Empty;
    }
    
    /// <summary>
    /// Gets the first error message or returns the default message if no errors exist
    /// </summary>
    public static string GetErrorMessage(this ResultBase result, string defaultMessage = "An error occurred")
    {
        return result.Errors.Count > 0 
            ? result.Errors[0].Message 
            : defaultMessage;
    }
}