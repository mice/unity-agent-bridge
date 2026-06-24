using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UnityAgentBridge.RoslynCompiler;

internal sealed class RoslynCompileService
{
    public CompileResponse Compile(CompileRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceText = ResolveSourceText(request);
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, path: request.SourcePath ?? "Entry.g.cs", cancellationToken: cancellationToken);

        var references = request.ReferenceProfile.References
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            request.AssemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputDllPath) ?? ".");
        using var dllStream = File.Create(request.OutputDllPath);
        var emitResult = compilation.Emit(dllStream, cancellationToken: cancellationToken);

        if (emitResult.Success)
        {
            return new CompileResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ExitStatus = "compiled",
                OutputDllPath = request.OutputDllPath,
                AssemblyName = request.AssemblyName,
                Diagnostics = emitResult.Diagnostics.Select(MapDiagnostic).ToList()
            };
        }

        dllStream.Close();
        if (File.Exists(request.OutputDllPath))
        {
            File.Delete(request.OutputDllPath);
        }

        var diagnostics = emitResult.Diagnostics.Select(MapDiagnostic).ToList();
        return new CompileResponse
        {
            RequestId = request.RequestId,
            Success = false,
            ExitStatus = "compile_failed",
            ErrorSummary = BuildErrorSummary(diagnostics),
            OutputDllPath = null,
            AssemblyName = request.AssemblyName,
            Diagnostics = diagnostics
        };
    }

    private static void ValidateRequest(CompileRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OutputDllPath))
        {
            throw new InvalidOperationException("outputDllPath is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AssemblyName))
        {
            throw new InvalidOperationException("assemblyName is required.");
        }

        if (request.ReferenceProfile == null)
        {
            throw new InvalidOperationException("referenceProfile is required.");
        }

        if (request.ReferenceProfile.References == null || request.ReferenceProfile.References.Count == 0)
        {
            throw new InvalidOperationException("referenceProfile.references must contain at least one path.");
        }
    }

    private static string ResolveSourceText(CompileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceText))
        {
            return request.SourceText;
        }

        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            return File.ReadAllText(request.SourcePath);
        }

        throw new InvalidOperationException("Either sourceText or an existing sourcePath is required.");
    }

    private static CompileDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var linePosition = location == Location.None || !location.IsInSource
            ? default
            : location.GetLineSpan().StartLinePosition;

        return new CompileDiagnostic
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Id = string.IsNullOrWhiteSpace(diagnostic.Id) ? null : diagnostic.Id,
            Message = diagnostic.GetMessage(),
            Line = location == Location.None || !location.IsInSource ? null : linePosition.Line + 1,
            Column = location == Location.None || !location.IsInSource ? null : linePosition.Character + 1
        };
    }

    private static string BuildErrorSummary(List<CompileDiagnostic> diagnostics)
    {
        var firstError = diagnostics.FirstOrDefault(d => string.Equals(d.Severity, "error", StringComparison.Ordinal));
        if (firstError == null)
        {
            return "compile_failed: no actionable diagnostic emitted";
        }

        var location = firstError.Line.HasValue && firstError.Column.HasValue
            ? $" at {firstError.Line}:{firstError.Column}"
            : string.Empty;
        return $"compile_failed: {firstError.Id}{location} - {firstError.Message}";
    }
}
