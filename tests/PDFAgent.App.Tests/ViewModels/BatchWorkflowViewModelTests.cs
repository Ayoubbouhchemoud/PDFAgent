using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PDFAgent.App.Services;
using PDFAgent.App.ViewModels;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PDFAgent.Core.Services;
using Xunit;

namespace PDFAgent.App.Tests.ViewModels;

public sealed class BatchWorkflowViewModelTests
{
    private static BatchWorkflowViewModel CreateVm(IBatchWorkflowService? service = null)
    {
        service ??= Substitute.For<IBatchWorkflowService>();
        var pdfEngine = Substitute.For<IPdfEngine>();
        return new BatchWorkflowViewModel(NullLogger<BatchWorkflowViewModel>.Instance, service, pdfEngine);
    }

    [Fact]
    public void InitialState_PipelineIsEmpty()
    {
        var vm = CreateVm();

        vm.Steps.Should().BeEmpty();
        vm.IsPipelineEmpty.Should().BeTrue();
        vm.CanRun.Should().BeFalse();
    }

    [Fact]
    public void AddStep_AddsToStepCollection()
    {
        var vm = CreateVm();
        var descriptor = StepTypeDescriptor.All[0];

        vm.AddStepCommand.Execute(descriptor);

        vm.Steps.Should().HaveCount(1);
        vm.IsPipelineEmpty.Should().BeFalse();
        vm.CanRun.Should().BeTrue();
    }

    [Fact]
    public void RemoveStep_RemovesFromCollection()
    {
        var vm = CreateVm();
        vm.AddStepCommand.Execute(StepTypeDescriptor.All[0]);
        var step = vm.Steps[0];

        vm.RemoveStepCommand.Execute(step);

        vm.Steps.Should().BeEmpty();
        vm.IsPipelineEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddEmptyStep_AddsFirstAvailableStepType()
    {
        var vm = CreateVm();

        vm.AddEmptyStepCommand.Execute(null);

        vm.Steps.Should().HaveCount(1);
        vm.Steps[0].DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunWorkflow_CallsServiceAndSetsIsRunning()
    {
        var service = Substitute.For<IBatchWorkflowService>();
        service.ExecuteAsync(Arg.Any<WorkflowPipeline>(), Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
               .Returns(OperationResult.Ok("Done"));

        var vm = CreateVm(service);
        vm.AddStepCommand.Execute(StepTypeDescriptor.All[0]);

        await vm.RunWorkflowCommand.ExecuteAsync(null);

        await service.Received(1).ExecuteAsync(
            Arg.Any<WorkflowPipeline>(),
            Arg.Any<IProgress<double>?>(),
            Arg.Any<CancellationToken>());

        vm.IsRunning.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Completed");
    }

    [Fact]
    public async Task RunWorkflow_WhenServiceFails_SetsErrorStatus()
    {
        var service = Substitute.For<IBatchWorkflowService>();
        service.ExecuteAsync(Arg.Any<WorkflowPipeline>(), Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
               .Returns(OperationResult.Fail("Engine error"));

        var vm = CreateVm(service);
        vm.AddStepCommand.Execute(StepTypeDescriptor.All[0]);

        await vm.RunWorkflowCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("Failed");
    }

    [Fact]
    public void SaveWorkflow_CallsServiceSavePipeline()
    {
        var service = Substitute.For<IBatchWorkflowService>();
        var vm = CreateVm(service);
        vm.WorkflowName = "My Pipeline";
        vm.AddStepCommand.Execute(StepTypeDescriptor.All[0]);

        vm.SaveWorkflowCommand.Execute(null);

        service.Received(1).SavePipeline(
            Arg.Is<WorkflowPipeline>(p => p.Name == "My Pipeline"),
            "My Pipeline");
    }
}
