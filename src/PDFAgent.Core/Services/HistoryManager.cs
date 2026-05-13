using PDFAgent.Core.Models;

namespace PDFAgent.Core.Services;

public interface IHistoryEntry
{
    string Description { get; }
    DateTime Timestamp { get; }
    Task<OperationResult> UndoAsync(CancellationToken ct = default);
    Task<OperationResult> RedoAsync(CancellationToken ct = default);
}

public sealed class HistoryManager
{
    private readonly LinkedList<IHistoryEntry> _entries = new();
    private LinkedListNode<IHistoryEntry>? _current;
    private const int MaxEntries = 100;

    public event EventHandler? HistoryChanged;

    public bool CanUndo => _current != null;
    public bool CanRedo => _current != null && _current.Next != null;
    public int UndoCount => _current?.List?.Count ?? 0;
    public int RedoCount => _current?.Next != null ? CountFrom(_current.Next) : 0;

    public void Push(IHistoryEntry entry)
    {
        if (_current != null && _current.Next != null)
        {
            var node = _current.Next;
            while (node != null)
            {
                var next = node.Next;
                _entries.Remove(node);
                node = next;
            }
        }

        _entries.AddLast(entry);
        _current = _entries.Last;

        while (_entries.Count > MaxEntries)
        {
            _entries.RemoveFirst();
            if (_current == _entries.First && _entries.First != null)
                _current = _entries.First.Next ?? _entries.First;
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        if (_current == null) return OperationResult.Fail("Nothing to undo");
        var result = await _current.Value.UndoAsync(ct);
        if (result.IsSuccess) _current = _current.Previous;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task<OperationResult> RedoAsync(CancellationToken ct = default)
    {
        if (_current?.Next == null) return OperationResult.Fail("Nothing to redo");
        var result = await _current.Next.Value.RedoAsync(ct);
        if (result.IsSuccess) _current = _current.Next;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void Clear()
    {
        _entries.Clear();
        _current = null;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int CountFrom(LinkedListNode<IHistoryEntry>? start)
    {
        var count = 0;
        var node = start;
        while (node != null) { count++; node = node.Next; }
        return count;
    }
}
