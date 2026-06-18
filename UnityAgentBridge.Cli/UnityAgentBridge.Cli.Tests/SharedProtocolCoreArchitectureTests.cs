using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class SharedProtocolCoreArchitectureTests
{
    private static readonly string[] ForbiddenSourceTokens =
    {
        "using UnityEngine",
        "using UnityEditor",
        "ModelContextProtocol",
        "System.CommandLine",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
        "System.Console",
        "Console.",
        "Environment.Exit",
        "System.Environment.Exit",
    };

    private static readonly string[] ForbiddenAssemblyReferences =
    {
        "UnityEngine",
        "UnityEditor",
        "ModelContextProtocol",
        "System.CommandLine",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
    };

    [TestMethod]
    public void CompatibilityProjectTargetsNetstandard20AndLinksPackageOwnedSources()
    {
        var repoRoot = ResolveRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "UnityAgentBridge.Cli", "UnityAgentBridge.SharedProtocolCore", "UnityAgentBridge.SharedProtocolCore.csproj");
        var document = XDocument.Load(projectPath);

        var targetFramework = document.Descendants().First(element => element.Name.LocalName == "TargetFramework").Value;
        Assert.AreEqual("netstandard2.0", targetFramework);

        var compileItems = document.Descendants().Where(element => element.Name.LocalName == "Compile").ToArray();
        Assert.AreEqual(1, compileItems.Length, "Expected a single linked compile item rooted at the package-owned SharedProtocolCore folder.");

        var include = compileItems[0].Attribute("Include")?.Value ?? string.Empty;
        var link = compileItems[0].Element(compileItems[0].Name.Namespace + "Link")?.Value ?? string.Empty;
        StringAssert.Contains(include, @"..\..\com.unitymcp.agent-bridge\Runtime\SharedProtocolCore\*.cs");
        StringAssert.Contains(link, @"SharedProtocolCore\");
    }

    [TestMethod]
    public void ExternalSolutionDoesNotOwnCopiedSharedProtocolSources()
    {
        var repoRoot = ResolveRepositoryRoot();
        var sharedProjectRoot = Path.Combine(repoRoot, "UnityAgentBridge.Cli", "UnityAgentBridge.SharedProtocolCore");
        var copiedSources = Directory.GetFiles(sharedProjectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, copiedSources.Length, "SharedProtocolCore sources must remain package-owned and linked into the external compatibility project.");
    }

    [TestMethod]
    public void SharedProtocolCoreRejectsForbiddenHostAndEngineDependencies()
    {
        var repoRoot = ResolveRepositoryRoot();
        var sourceRoot = Path.Combine(repoRoot, "com.unitymcp.agent-bridge", "Runtime", "SharedProtocolCore");
        var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.IsTrue(sourceFiles.Length > 0, "SharedProtocolCore source files must exist.");

        foreach (var sourceFile in sourceFiles)
        {
            var sourceText = File.ReadAllText(sourceFile);
            foreach (var forbiddenToken in ForbiddenSourceTokens)
            {
                Assert.IsFalse(
                    sourceText.Contains(forbiddenToken, StringComparison.Ordinal),
                    $"SharedProtocolCore source '{Path.GetFileName(sourceFile)}' must not depend on '{forbiddenToken}'.");
            }
        }

        var referencedAssemblyNames = typeof(UnityMcp.AgentBridge.AgentCommand).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var forbiddenReference in ForbiddenAssemblyReferences)
        {
            CollectionAssert.DoesNotContain(
                referencedAssemblyNames,
                forbiddenReference,
                $"SharedProtocolCore assembly must not reference '{forbiddenReference}'.");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        for (var cursor = new DirectoryInfo(AppContext.BaseDirectory); cursor != null; cursor = cursor.Parent)
        {
            var hasCliFolder = Directory.Exists(Path.Combine(cursor.FullName, "UnityAgentBridge.Cli"));
            var hasPackageFolder = Directory.Exists(Path.Combine(cursor.FullName, "com.unitymcp.agent-bridge"));
            if (hasCliFolder && hasPackageFolder)
            {
                return cursor.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root from the test AppContext base directory.");
    }
}
