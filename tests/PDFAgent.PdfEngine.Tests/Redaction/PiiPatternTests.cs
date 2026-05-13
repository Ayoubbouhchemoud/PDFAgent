using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace PDFAgent.PdfEngine.Tests.Redaction;

public class PiiPatternTests
{
    private const string EmailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
    private const string SsnPattern = @"\b\d{3}-\d{2}-\d{4}\b";
    private const string CreditCardPattern = @"\b(?:\d[ -]*?){13,16}\b";
    private const string PhonePattern = @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b";

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("first.last@company.co.uk", true)]
    [InlineData("notanemail", false)]
    [InlineData("@invalid", false)]
    public void EmailPattern_ShouldMatchExpected(string input, bool shouldMatch)
    {
        Regex.IsMatch(input, EmailPattern, RegexOptions.IgnoreCase)
            .Should().Be(shouldMatch);
    }

    [Theory]
    [InlineData("123-45-6789", true)]
    [InlineData("000-00-0000", true)]
    [InlineData("12345-6789", false)]
    [InlineData("123-456-789", false)]
    public void SsnPattern_ShouldMatchExpected(string input, bool shouldMatch)
    {
        Regex.IsMatch(input, SsnPattern).Should().Be(shouldMatch);
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("4111-1111-1111-1111", true)]
    [InlineData("1234", false)]
    public void CreditCardPattern_ShouldMatchExpected(string input, bool shouldMatch)
    {
        Regex.IsMatch(input.Replace(" ", "").Replace("-", ""), CreditCardPattern)
            .Should().Be(shouldMatch);
    }

    [Theory]
    [InlineData("555-123-4567", true)]
    [InlineData("555.123.4567", true)]
    [InlineData("5551234567", true)]
    [InlineData("123", false)]
    public void PhonePattern_ShouldMatchExpected(string input, bool shouldMatch)
    {
        Regex.IsMatch(input, PhonePattern).Should().Be(shouldMatch);
    }

    [Fact]
    public void CombinedPiiPattern_ShouldMatchAllTypes()
    {
        var text = "Contact user@test.com or call 555-123-4567. SSN: 123-45-6789";
        var patterns = new[] { EmailPattern, PhonePattern, SsnPattern };
        var combined = string.Join("|", patterns);

        var matches = Regex.Matches(text, combined, RegexOptions.IgnoreCase);
        matches.Count.Should().Be(3);
    }
}
