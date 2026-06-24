using Newtonsoft.Json;

namespace UnityAgentBridge.RoslynCompiler;

internal sealed class CompileRequest
{
    [JsonProperty("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonProperty("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonProperty("sourceText")]
    public string? SourceText { get; set; }

    [JsonProperty("outputDllPath")]
    public string OutputDllPath { get; set; } = string.Empty;

    [JsonProperty("assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [JsonProperty("referenceProfile")]
    public ReferenceProfile ReferenceProfile { get; set; } = new();

    [JsonProperty("timeoutMs")]
    public int TimeoutMs { get; set; }
}

internal sealed class ReferenceProfile
{
    [JsonProperty("profileId")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonProperty("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    [JsonProperty("references")]
    public List<string> References { get; set; } = new();
}

internal sealed class CompileResponse
{
    [JsonProperty("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("exitStatus")]
    public string ExitStatus { get; set; } = string.Empty;

    [JsonProperty("errorSummary")]
    public string ErrorSummary { get; set; } = string.Empty;

    [JsonProperty("outputDllPath")]
    public string? OutputDllPath { get; set; }

    [JsonProperty("assemblyName")]
    public string? AssemblyName { get; set; }

    [JsonProperty("diagnostics")]
    public List<CompileDiagnostic> Diagnostics { get; set; } = new();
}

internal sealed class CompileDiagnostic
{
    [JsonProperty("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("line")]
    public int? Line { get; set; }

    [JsonProperty("column")]
    public int? Column { get; set; }
}
