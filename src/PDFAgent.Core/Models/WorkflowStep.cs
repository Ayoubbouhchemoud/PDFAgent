namespace PDFAgent.Core.Models;

public enum WorkflowStepType
{
    OcrAllPages,
    Redact,
    Rotate,
    Compress,
    AddWatermark,
    ExtractPages,
    MergeFiles,
    ConvertToImage,
    ApplyStamp,
}

public sealed class WorkflowStep
{
    public WorkflowStepType StepType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public sealed class WorkflowPipeline
{
    public string Name { get; set; } = "Untitled";
    public List<WorkflowStep> Steps { get; set; } = new();
    public string DocumentPath { get; set; } = string.Empty;
}

/// <summary>Descriptor used by the UI step library palette.</summary>
public sealed class StepTypeDescriptor
{
    public WorkflowStepType StepType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconData { get; init; } = string.Empty;

    public static readonly IReadOnlyList<StepTypeDescriptor> All = new List<StepTypeDescriptor>
    {
        new() { StepType = WorkflowStepType.OcrAllPages,   DisplayName = "OCR All Pages",    Description = "Run OCR on every page and embed text layer",      IconData = "M 3,3 L 3,8 M 3,3 L 8,3 M 21,3 L 16,3 M 21,3 L 21,8 M 3,21 L 3,16 M 3,21 L 8,21 M 21,21 L 16,21 M 21,21 L 21,16 M 7,10 L 17,10 M 7,12 L 17,12 M 7,14 L 14,14" },
        new() { StepType = WorkflowStepType.Redact,        DisplayName = "Redact PII",        Description = "Automatically find and black-out sensitive data",   IconData = "M 3,8 L 21,8 L 21,12 L 3,12 Z M 6,14 L 18,14 M 6,16 L 14,16" },
        new() { StepType = WorkflowStepType.Rotate,        DisplayName = "Rotate Pages",      Description = "Rotate all or selected pages by 90 / 180°",        IconData = "M 19,8 A 7,7 0 1 0 17,15 M 19,8 L 19,4 L 15,8 Z" },
        new() { StepType = WorkflowStepType.Compress,      DisplayName = "Compress",          Description = "Reduce file size while preserving quality",         IconData = "M 12,4 L 12,14 M 8,10 L 12,14 L 16,10 M 4,18 L 20,18" },
        new() { StepType = WorkflowStepType.AddWatermark,  DisplayName = "Add Watermark",     Description = "Overlay a text or image watermark on each page",    IconData = "M 4,20 L 8,20 L 8,18 L 4,18 Z M 14,4 L 20,10 L 9,21 L 3,21 L 3,15 Z" },
        new() { StepType = WorkflowStepType.ExtractPages,  DisplayName = "Extract Pages",     Description = "Save a page range as a new document",               IconData = "M 12,3 L 12,15 M 8,7 L 12,3 L 16,7 M 5,17 L 5,21 L 19,21 L 19,17" },
        new() { StepType = WorkflowStepType.MergeFiles,    DisplayName = "Merge Files",       Description = "Combine multiple PDFs into one",                    IconData = "M 8,4 L 12,8 L 8,12 M 16,4 L 12,8 M 12,8 L 12,20" },
        new() { StepType = WorkflowStepType.ConvertToImage, DisplayName = "Convert to Images", Description = "Export each page as PNG/JPEG",                     IconData = "M 3,5 Q 3,3 5,3 L 19,3 Q 21,3 21,5 L 21,19 Q 21,21 19,21 L 5,21 Q 3,21 3,19 Z M 3,15 L 8,10 L 12,14 L 15,11 L 21,17" },
        new() { StepType = WorkflowStepType.ApplyStamp,    DisplayName = "Apply Stamp",       Description = "Add APPROVED / DRAFT / CONFIDENTIAL stamp",         IconData = "M 4,4 Q 4,2 6,2 L 18,2 Q 20,2 20,4 L 20,14 Q 20,16 18,16 L 10,16 L 4,20 L 4,16 Q 2,16 2,14 Z" },
    };
}
