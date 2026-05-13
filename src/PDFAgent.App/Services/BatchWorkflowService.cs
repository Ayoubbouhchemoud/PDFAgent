using Microsoft.Extensions.Logging;
using PDFAgent.Core.Models;
using PDFAgent.Core.Services;

namespace PDFAgent.App.Services;

/// <summary>
/// Stub implementation — executes each step sequentially and reports progress.
/// Real per-step logic is plugged in by registering IStepExecutor handlers.
/// </summary>
public sealed class BatchWorkflowService : IBatchWorkflowService
{
    private readonly ILogger<BatchWorkflowService> _logger;
    private readonly List<WorkflowPipeline> _saved = new();

    public BatchWorkflowService(ILogger<BatchWorkflowService> logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult> ExecuteAsync(
        WorkflowPipeline pipeline,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (pipeline.Steps.Count == 0)
            return OperationResult.Fail("Pipeline has no steps");

        _logger.LogInformation("Executing workflow \"{Name}\" with {Count} steps",
            pipeline.Name, pipeline.Steps.Count);

        for (var i = 0; i < pipeline.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = pipeline.Steps[i];
            _logger.LogInformation("Running step {Index}: {Step}", i + 1, step.DisplayName);

            // Simulate async step work
            await Task.Delay(120, ct);

            progress?.Report((double)(i + 1) / pipeline.Steps.Count);
        }

        return OperationResult.Ok("Workflow completed successfully");
    }

    public void SavePipeline(WorkflowPipeline pipeline, string name)
    {
        pipeline.Name = name;
        var existing = _saved.FindIndex(p => p.Name == name);
        if (existing >= 0)
            _saved[existing] = pipeline;
        else
            _saved.Add(pipeline);

        _logger.LogInformation("Saved pipeline \"{Name}\"", name);
    }

    public IReadOnlyList<WorkflowPipeline> LoadSavedPipelines() => _saved.AsReadOnly();
}
