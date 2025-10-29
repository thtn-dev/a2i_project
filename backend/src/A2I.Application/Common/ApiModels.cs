namespace A2I.Application.Common;

/// <summary>
///     Standard API response wrapper for successful responses
/// </summary>
/// <typeparam name="T">Type of data being returned</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public T? Data { get; set; }
    public string? Message { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }
}

/// <summary>
///     Error response for failed requests
/// </summary>
public class ErrorResponse
{
    public bool Success { get; set; } = false;
    public ErrorDetail Error { get; set; } = null!;

    public static ErrorResponse Create(string code, string message,
        Dictionary<string, string[]>? validationErrors = null, string? traceId = null)
    {
        return new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = code,
                Message = message,
                ValidationErrors = validationErrors,
                TraceId = traceId
            }
        };
    }
}

/// <summary>
///     Detailed error information
/// </summary>
public class ErrorDetail
{
    public string Code { get; set; } = null!;
    public string Message { get; set; } = null!;
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
    public string? TraceId { get; set; }
}

/// <summary>
///     Paginated response wrapper
/// </summary>
/// <typeparam name="T">Type of items in the collection</typeparam>
public class PaginatedResponse<T>
{
    public bool Success { get; set; } = true;
    public PaginatedData<T> Data { get; set; } = null!;

    public static PaginatedResponse<T> Ok(List<T> items, PaginationMetadata pagination)
    {
        return new PaginatedResponse<T>
        {
            Success = true,
            Data = new PaginatedData<T>
            {
                Items = items,
                Pagination = pagination
            }
        };
    }
}

/// <summary>
///     Container for paginated data
/// </summary>
public class PaginatedData<T>
{
    public List<T> Items { get; set; } = new();
    public PaginationMetadata Pagination { get; set; } = null!;
}

/// <summary>
///     Pagination metadata (reuse from existing Dto.cs)
/// </summary>
public class PaginationMetadata
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}