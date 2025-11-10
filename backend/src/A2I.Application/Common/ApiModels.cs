namespace A2I.Application.Common;

public class ApiResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }

    public static ApiResponse Ok(string? message = null)
    {
        return new ApiResponse
        {
            Success = true,
            Message = message
        };
    }
}

/// <summary>
///     Standard API response wrapper for successful responses
/// </summary>
/// <typeparam name="T">Type of data being returned</typeparam>
public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }
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
///     Paginated response wrapper
/// </summary>
/// <typeparam name="T">Type of items in the collection</typeparam>
public class PaginatedResponse<T>
{
    public bool Success { get; set; } = true;
    public PaginatedData<T> Data { get; set; } = null!;

    public static PaginatedResponse<T> Ok(IReadOnlyList<T> items, PaginationMetadata pagination)
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
    public IReadOnlyList<T> Items { get; set; } = [];
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