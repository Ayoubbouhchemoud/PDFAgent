using System.Text.Json;

namespace PDFAgent.Core.Configuration;

public sealed record AppConfiguration
{
    public PdfEngineConfig PdfEngine { get; init; } = new();
    public OcrConfig Ocr { get; init; } = new();
    public UiConfig Ui { get; init; } = new();
    public SecurityConfig Security { get; init; } = new();
    public ScriptingConfig Scripting { get; init; } = new();
    public CloudConfig Cloud { get; init; } = new();
    public PrivacyConfig Privacy { get; init; } = new();

    public static AppConfiguration Load(string path)
    {
        if (!File.Exists(path)) return new AppConfiguration();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDFAgent", "config.json");
}

public sealed record PdfEngineConfig
{
    public int MaxCachedPages { get; init; } = 50;
    public int RenderThreads { get; init; } = 4;
    public int ThumbnailSize { get; init; } = 256;
    public double DefaultDpi { get; init; } = 150;
    public string? TempDir { get; init; }
}

public sealed record OcrConfig
{
    public string? DataPath { get; init; }
    public string DefaultLanguage { get; init; } = "eng";
    public bool UseGpuAcceleration { get; init; }
    public int MaxParallelPages { get; init; } = 2;
}

public sealed record UiConfig
{
    public string Theme { get; init; } = "FluentDark";
    public double DefaultZoom { get; init; } = 1.0;
    public bool ShowThumbnails { get; init; } = true;
    public bool EnableHardwareAcceleration { get; init; } = true;
}

public sealed record SecurityConfig
{
    public bool RequireAdminForInstall { get; init; } = true;
    public bool AuditRedactions { get; init; } = true;
    public string? AuditLogPath { get; init; }
    public bool SandboxParsing { get; init; } = true;
}

public sealed record ScriptingConfig
{
    public bool EnableScripting { get; init; } = true;
    public bool SandboxScripts { get; init; } = true;
    public int ScriptTimeoutSeconds { get; init; } = 30;
}

public sealed record CloudConfig
{
    public bool EnableCloudFeatures { get; init; }
    public bool EnableOcrImprovement { get; init; }
    public bool EnableLlmAssist { get; init; }
    public string? ApiEndpoint { get; init; }
}

public sealed record PrivacyConfig
{
    public bool LocalProcessingOnly { get; init; } = true;
    public bool SendUsageData { get; init; }
    public bool EnableTelemetry { get; init; }
    public bool LogRedactionDetails { get; init; } = true;
}
