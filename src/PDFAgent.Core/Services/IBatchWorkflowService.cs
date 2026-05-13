using PDFAgent.Core.Models;

namespace PDFAgent.Core.Services;

public interface IBatchWorkflowService
{
    Task<OperationResult> ExecuteAsync(
        WorkflowPipeline pipeline,
        IProgress<double>? progress,
        CancellationToken ct);

    void SavePipeline(WorkflowPipeline pipeline, string name);

    IReadOnlyList<WorkflowPipeline> LoadSavedPipelines();
}
