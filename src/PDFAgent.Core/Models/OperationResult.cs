namespace PDFAgent.Core.Models;

public record OperationResult
{
    public bool IsSuccess { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public Exception? Exception { get; init; }

    public static OperationResult Ok(string? message = null) =>
        new() { IsSuccess = true, Message = message };

    public static OperationResult Fail(string message, string? errorCode = null, Exception? ex = null) =>
        new() { IsSuccess = false, Message = message, ErrorCode = errorCode, Exception = ex };

    public static OperationResult<T> Ok<T>(T value, string? message = null) =>
        new() { IsSuccess = true, Value = value, Message = message };

    public static OperationResult<T> Fail<T>(string message, string? errorCode = null, Exception? ex = null) =>
        new() { IsSuccess = false, Message = message, ErrorCode = errorCode, Exception = ex };
}

public sealed record OperationResult<T> : OperationResult
{
    public T? Value { get; init; }
    public static implicit operator OperationResult<T>(T value) => Ok(value);
}
