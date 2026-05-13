using FluentAssertions;
using PDFAgent.Core.Models;
using Xunit;

namespace PDFAgent.Core.Tests.Models;

public sealed class WorkflowStepTests
{
    [Fact]
    public void StepTypeDescriptor_All_ContainsExpectedStepTypes()
    {
        var types = StepTypeDescriptor.All.Select(d => d.StepType).ToList();

        types.Should().Contain(WorkflowStepType.OcrAllPages);
        types.Should().Contain(WorkflowStepType.Redact);
        types.Should().Contain(WorkflowStepType.Compress);
        types.Should().Contain(WorkflowStepType.MergeFiles);
    }

    [Fact]
    public void StepTypeDescriptor_All_EachHasNonEmptyDisplayName()
    {
        foreach (var d in StepTypeDescriptor.All)
            d.DisplayName.Should().NotBeNullOrWhiteSpace(
                because: $"{d.StepType} must have a display name");
    }

    [Fact]
    public void StepTypeDescriptor_All_EachHasIconData()
    {
        foreach (var d in StepTypeDescriptor.All)
            d.IconData.Should().NotBeNullOrWhiteSpace(
                because: $"{d.StepType} must have path icon data");
    }

    [Fact]
    public void WorkflowPipeline_DefaultName_IsUntitled()
    {
        var pipeline = new WorkflowPipeline();
        pipeline.Name.Should().Be("Untitled");
    }

    [Fact]
    public void WorkflowPipeline_Steps_DefaultsToEmptyList()
    {
        var pipeline = new WorkflowPipeline();
        pipeline.Steps.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void WorkflowStep_Parameters_DefaultsToEmptyDictionary()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Rotate,
            DisplayName = "Rotate",
        };

        step.Parameters.Should().NotBeNull().And.BeEmpty();
    }
}
