using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using UnityAgentBridge.RoslynCompiler;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class RoslynCompilerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [TestMethod]
    public void Compile_ValidSource_ProducesAssembly()
    {
        var service = new RoslynCompileService();
        var outputPath = Path.Combine(_root, "out", "test.dll");
        var request = new CompileRequest
        {
            RequestId = "req-valid",
            AssemblyName = "RuntimeScriptValid",
            OutputDllPath = outputPath,
            SourceText = "public static class Entry { public static object Run() { return 42; } }",
            ReferenceProfile = CreateTrustedRuntimeProfile()
        };

        var response = service.Compile(request, CancellationToken.None);

        Assert.IsTrue(response.Success);
        Assert.AreEqual("compiled", response.ExitStatus);
        Assert.AreEqual(outputPath, response.OutputDllPath);
        Assert.IsTrue(File.Exists(outputPath));

        var assembly = Assembly.Load(File.ReadAllBytes(outputPath));
        var runMethod = assembly.GetType("Entry")?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(runMethod);
        Assert.AreEqual(42, runMethod.Invoke(null, null));
    }

    [TestMethod]
    public void Compile_InvalidSource_ReturnsStructuredDiagnostics()
    {
        var service = new RoslynCompileService();
        var request = new CompileRequest
        {
            RequestId = "req-invalid",
            AssemblyName = "RuntimeScriptInvalid",
            OutputDllPath = Path.Combine(_root, "out", "invalid.dll"),
            SourceText = "public static class Entry { public static object Run() { return ; } }",
            ReferenceProfile = CreateTrustedRuntimeProfile()
        };

        var response = service.Compile(request, CancellationToken.None);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("compile_failed", response.ExitStatus);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorSummary));
        Assert.IsTrue(response.Diagnostics.Count > 0);
        Assert.IsTrue(response.Diagnostics.Exists(d => string.Equals(d.Severity, "error", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Compile_MissingReferenceProfileEntries_ReturnsCompileFailure()
    {
        var service = new RoslynCompileService();
        var request = new CompileRequest
        {
            RequestId = "req-missing-ref",
            AssemblyName = "RuntimeScriptMissingReference",
            OutputDllPath = Path.Combine(_root, "out", "missing-ref.dll"),
            SourceText = "using System.Linq; public static class Entry { public static object Run() { return Enumerable.Range(0, 3).ToArray(); } }",
            ReferenceProfile = new ReferenceProfile
            {
                ProfileId = "minimal",
                UnityVersion = "test",
                References = new List<string>
                {
                    typeof(object).Assembly.Location
                }
            }
        };

        var response = service.Compile(request, CancellationToken.None);

        Assert.IsFalse(response.Success);
        Assert.AreEqual("compile_failed", response.ExitStatus);
        Assert.IsTrue(response.Diagnostics.Exists(d => string.Equals(d.Severity, "error", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Compile_InvalidRequest_Throws()
    {
        var service = new RoslynCompileService();
        var request = new CompileRequest
        {
            RequestId = "req-invalid-request",
            AssemblyName = string.Empty,
            OutputDllPath = string.Empty,
            SourceText = "public static class Entry { }",
            ReferenceProfile = new ReferenceProfile()
        };

        Assert.ThrowsException<InvalidOperationException>(() => service.Compile(request, CancellationToken.None));
    }

    [TestMethod]
    public void Compile_CancelledToken_ThrowsOperationCanceledException()
    {
        var service = new RoslynCompileService();
        var request = new CompileRequest
        {
            RequestId = "req-timeout",
            AssemblyName = "RuntimeScriptCancelled",
            OutputDllPath = Path.Combine(_root, "out", "cancelled.dll"),
            SourceText = "public static class Entry { public static object Run() { return 1; } }",
            ReferenceProfile = CreateTrustedRuntimeProfile()
        };
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() => service.Compile(request, cancellationTokenSource.Token));
    }

    private static ReferenceProfile CreateTrustedRuntimeProfile()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.IsFalse(string.IsNullOrWhiteSpace(tpa));

        return new ReferenceProfile
        {
            ProfileId = "trusted-platform",
            UnityVersion = "test",
            References = tpa!
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
