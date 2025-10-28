namespace A2I.Application.Common.Caching;

public enum CacheResultType
{
    Hit,
    Miss,
    Error
}

public class CacheResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public CacheResultType ResultType { get; }

    private CacheResult(bool success, T? value, string? errorMessage, CacheResultType resultType)
    {
        Success = success;
        Value = value;
        ErrorMessage = errorMessage;
        ResultType = resultType;
    }

    public static CacheResult<T> Hit(T value) 
        => new(true, value, null, CacheResultType.Hit);

    public static CacheResult<T> Miss() 
        => new(false, default, null, CacheResultType.Miss);

    public static CacheResult<T> Error(string errorMessage) 
        => new(false, default, errorMessage, CacheResultType.Error);

    public bool IsHit => ResultType == CacheResultType.Hit;
    public bool IsMiss => ResultType == CacheResultType.Miss;
    public bool IsError => ResultType == CacheResultType.Error;
}