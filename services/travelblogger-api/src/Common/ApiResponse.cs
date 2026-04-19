namespace TravelBlogger.Common;

public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string[] Errors { get; init; } = Array.Empty<string>();

    public static ApiResponse Ok(string message = "OK") => new() { Success = true, Message = message };
    public static ApiResponse Fail(string message, string[]? errors = null) => new() { Success = false, Message = message, Errors = errors ?? Array.Empty<string>() };
}

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public string[] Errors { get; init; } = Array.Empty<string>();

    public static ApiResponse<T> Ok(T data, string message = "OK") => new() { Success = true, Data = data, Message = message };
    public static ApiResponse<T> Fail(string message, string[]? errors = null) => new() { Success = false, Message = message, Errors = errors ?? Array.Empty<string>() };
}
