using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDFAgent.Core.Interfaces;
using PDFAgent.Core.Models;
using PDFAgent.Core.Services;

namespace PDFAgent.App.ViewModels;

public sealed partial class BatchWorkflowViewModel : ObservableObject
{
    private readonly ILogger<BatchWorkflowViewModel> _logger;
    private readonly IBatchWorkflowService _workflowService;
    private readonly IPdfEngine _pdfEngine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPipelineEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RunWorkflowCommand))]
    private ObservableCollection<WorkflowStepViewModel> _steps = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunWorkflowCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private double _runProgress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _workflowName = "New Workflow";

    public bool IsPipelineEmpty => Steps.Count == 0;
    public bool CanRun => Steps.Count > 0 && !IsRunning;

    public IReadOnlyList<StepTypeDescriptor> AvailableStepTypes { get; } = StepTypeDescriptor.All;

    public BatchWorkflowViewModel(
        ILogger<BatchWorkflowViewModel> logger,
        IBatchWorkflowService workflowService,
        IPdfEngine pdfEngine)
    {
        _logger = logger;
        _workflowService = workflowService;
        _pdfEngine = pdfEngine;
    }

    [RelayCommand]
    private void AddStep(StepTypeDescriptor descriptor)
    {
        Steps.Add(new WorkflowStepViewModel(descriptor));
        OnPropertyChanged(nameof(IsPipelineEmpty));
        RunWorkflowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddEmptyStep() => AddStep(AvailableStepTypes[0]);

    [RelayCommand]
    private void RemoveStep(WorkflowStepViewModel step)
    {
        Steps.Remove(step);
        OnPropertyChanged(nameof(IsPipelineEmpty));
        RunWorkflowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunWorkflowAsync()
    {
        IsRunning = true;
        RunProgress = 0;
        StatusMessage = "Running workflow…";

        try
        {
            var pipeline = BuildPipeline();
            var progress = new Progress<double>(p =>
            {
                RunProgress = p * 100;
                var stepIdx = (int)(p * Steps.Count);
                StatusMessage = stepIdx < Steps.Count
                    ? $"Step {stepIdx + 1}/{Steps.Count}: {Steps[Math.Min(stepIdx, Steps.Count - 1)].DisplayName}…"
                    : $"Finishing…";
            });

            var result = await _workflowService.ExecuteAsync(pipeline, progress, CancellationToken.None);
            StatusMessage = result.IsSuccess
                ? $"Completed — {result.Message}"
                : $"Failed: {result.Message}";
            RunProgress = 100;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void SaveWorkflow()
    {
        var pipeline = BuildPipeline();
        _workflowService.SavePipeline(pipeline, WorkflowName);
        StatusMessage = $"Saved \"{WorkflowName}\"";
    }

    private WorkflowPipeline BuildPipeline() => new()
    {
        Name = WorkflowName,
        Steps = Steps.Select(s => s.ToModel()).ToList(),
        DocumentPath = _pdfEngine.IsOpen ? _pdfEngine.FilePath : string.Empty,
    };
}

public sealed partial class WorkflowStepViewModel : ObservableObject
{
    private readonly StepTypeDescriptor _descriptor;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _summary;

    public string IconData => _descriptor.IconData;

    public WorkflowStepViewModel(StepTypeDescriptor descriptor)
    {
        _descriptor = descriptor;
        _displayName = descriptor.DisplayName;
        _summary = descriptor.Description;
    }

    public WorkflowStep ToModel() => new()
    {
        StepType = _descriptor.StepType,
        DisplayName = DisplayName,
        Parameters = new Dictionary<string, object>(),
    };
}
