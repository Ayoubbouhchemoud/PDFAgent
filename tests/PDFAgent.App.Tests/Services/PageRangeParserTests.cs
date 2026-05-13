using FluentAssertions;
using PDFAgent.App.Services;
using Xunit;

namespace PDFAgent.App.Tests.Services;

public sealed class PageRangeParserTests
{
    private const int TotalPages = 10;

    [Fact]
    public void EmptyString_ReturnsAllPages()
    {
        var result = PageRangeParser.Parse(string.Empty, TotalPages);
        result.Should().BeEquivalentTo(Enumerable.Range(0, TotalPages));
    }

    [Fact]
    public void WhiteSpace_ReturnsAllPages()
    {
        var result = PageRangeParser.Parse("   ", TotalPages);
        result.Should().BeEquivalentTo(Enumerable.Range(0, TotalPages));
    }

    [Fact]
    public void AllKeyword_ReturnsAllPages()
    {
        var result = PageRangeParser.Parse("all", TotalPages);
        result.Should().BeEquivalentTo(Enumerable.Range(0, TotalPages));
    }

    [Fact]
    public void SinglePage_ReturnsOneZeroBasedIndex()
    {
        var result = PageRangeParser.Parse("3", TotalPages);
        result.Should().BeEquivalentTo(new[] { 2 }, "page 3 → 0-based index 2");
    }

    [Fact]
    public void CommaSeparated_ReturnsMultipleIndices()
    {
        var result = PageRangeParser.Parse("1,3,5", TotalPages);
        result.Should().BeEquivalentTo(new[] { 0, 2, 4 });
    }

    [Fact]
    public void Range_ReturnsContiguousIndices()
    {
        var result = PageRangeParser.Parse("2-4", TotalPages);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void MixedCommaAndRange_ReturnsMergedSet()
    {
        var result = PageRangeParser.Parse("1,3-5,7", TotalPages);
        result.Should().BeEquivalentTo(new[] { 0, 2, 3, 4, 6 });
    }

    [Fact]
    public void DuplicatePages_DeduplicatedInResult()
    {
        var result = PageRangeParser.Parse("1,1,1-3,2", TotalPages);
        result.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    [Fact]
    public void PageOutOfRange_ClampedToValidBound()
    {
        var result = PageRangeParser.Parse("0,99", TotalPages);
        result.Should().BeEquivalentTo(new[] { 0, 9 },
            "0 clamps to page 1 (index 0), 99 clamps to page 10 (index 9)");
    }

    [Fact]
    public void InvalidText_ReturnsEmptyList()
    {
        var result = PageRangeParser.Parse("abc,xyz", TotalPages);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ReversedRange_ParsedCorrectly()
    {
        var result = PageRangeParser.Parse("5-3", TotalPages);
        result.Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }

    [Fact]
    public void Page1_ReturnsZeroBasedIndex0()
    {
        var result = PageRangeParser.Parse("1", TotalPages);
        result.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void LastPage_ReturnsCorrectIndex()
    {
        var result = PageRangeParser.Parse("10", TotalPages);
        result.Should().BeEquivalentTo(new[] { 9 });
    }
}
