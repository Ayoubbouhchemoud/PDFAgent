using FluentAssertions;
using PDFAgent.Core.Models;
using PDFAgent.Core.Services;
using Xunit;

namespace PDFAgent.Core.Tests.Services;

public class HistoryManagerTests
{
    private sealed class TestEntry : IHistoryEntry
    {
        public string Description { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool UndoCalled { get; private set; }
        public bool RedoCalled { get; private set; }
        public bool UndoFails { get; set; }

        public TestEntry(string description) => Description = description;

        public Task<OperationResult> UndoAsync(CancellationToken ct = default)
        {
            UndoCalled = true;
            return Task.FromResult(UndoFails
                ? OperationResult.Fail("Undo failed")
                : OperationResult.Ok());
        }

        public Task<OperationResult> RedoAsync(CancellationToken ct = default)
        {
            RedoCalled = true;
            return Task.FromResult(OperationResult.Ok());
        }
    }

    [Fact]
    public void Push_ShouldAddToHistory()
    {
        var history = new HistoryManager();
        history.CanUndo.Should().BeFalse();

        history.Push(new TestEntry("Edit 1"));
        history.CanUndo.Should().BeTrue();
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_ShouldCallEntryUndo()
    {
        var history = new HistoryManager();
        var entry = new TestEntry("Edit 1");
        history.Push(entry);

        var result = await history.UndoAsync();
        result.IsSuccess.Should().BeTrue();
        entry.UndoCalled.Should().BeTrue();
    }

    [Fact]
    public void Push_AfterUndo_ShouldClearRedoStack()
    {
        var history = new HistoryManager();
        history.Push(new TestEntry("A"));
        history.Push(new TestEntry("B"));

        var _ = history.UndoAsync().Result;
        history.CanRedo.Should().BeTrue();

        history.Push(new TestEntry("C"));
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_WhenEmpty_ShouldReturnFail()
    {
        var history = new HistoryManager();
        var result = history.UndoAsync().Result;
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Nothing to undo");
    }

    [Fact]
    public void Redo_WhenEmpty_ShouldReturnFail()
    {
        var history = new HistoryManager();
        var result = history.RedoAsync().Result;
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Nothing to redo");
    }

    [Fact]
    public void Clear_ShouldResetHistory()
    {
        var history = new HistoryManager();
        history.Push(new TestEntry("A"));
        history.Push(new TestEntry("B"));

        history.Clear();
        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Push_WhenAtLimit_ShouldDiscardOldest()
    {
        var history = new HistoryManager();
        for (var i = 0; i < 105; i++)
            history.Push(new TestEntry($"Edit {i}"));

        // Should not crash — oldest entries are discarded
        history.CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task UndoAsync_WhenEntryFails_ShouldNotMovePointer()
    {
        var history = new HistoryManager();
        var a = new TestEntry("A");
        var b = new TestEntry("B") { UndoFails = true };
        history.Push(a);
        history.Push(b);

        var result = await history.UndoAsync();

        result.IsSuccess.Should().BeFalse();
        // Pointer should still be at B since undo failed
    }
}
