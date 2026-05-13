using FluentAssertions;
using PDFAgent.Core.Models;
using Xunit;

namespace PDFAgent.Core.Tests.Models;

public class OperationResultTests
{
    [Fact]
    public void Ok_ShouldCreateSuccessResult()
    {
        var result = OperationResult.Ok("Success");
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Success");
    }

    [Fact]
    public void Fail_ShouldCreateFailureResult()
    {
        var result = OperationResult.Fail("Error occurred", "ERR_001");
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Error occurred");
        result.ErrorCode.Should().Be("ERR_001");
    }

    [Fact]
    public void Ok_WithValue_ShouldContainValue()
    {
        var result = OperationResult.Ok(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateOk()
    {
        OperationResult<int> result = 100;
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(100);
    }

    [Fact]
    public void Fail_Generic_ShouldCreateFailure()
    {
        var result = OperationResult.Fail<string>("Failed");
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
    }
}
