namespace PDFAgent.Core.Services;

public sealed class OperationContext : IDisposable
{
    private readonly IProgress<double>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly IReadOnlyDictionary<string, object>? _metadata;

    public OperationContext(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        _progress = progress;
        _cancellationToken = cancellationToken;
        _metadata = metadata;
    }

    public CancellationToken Token => _cancellationToken;
    public bool IsCancellationRequested => _cancellationToken.IsCancellationRequested;

    public void ReportProgress(double value) => _progress?.Report(value);

    public void ThrowIfCancelled() => _cancellationToken.ThrowIfCancellationRequested();

    public T? GetMetadata<T>(string key) where T : class
    {
        if (_metadata?.TryGetValue(key, out var val) == true && val is T typed)
            return typed;
        return null;
    }

    public void Dispose() { }
}
