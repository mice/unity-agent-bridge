using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpServerRuntimeTests
{
    [TestMethod]
    public async Task McpServerStartup_DoesNotWriteHostingLogsToStdout()
    {
        var projectRoot = CreateUnityProject();
        var assemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "UnityAgentBridge.Cli",
            "bin",
            "Debug",
            "net8.0",
            "win-x64",
            "UnityAgentBridge.Cli.dll"));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{assemblyPath}\" mcp-server",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.Environment["UNITY_AGENT_BRIDGE_PROJECT_PATH"] = projectRoot;
        process.Start();

        try
        {
            await Task.Delay(1000);
            var stdout = await ReadAvailableAsync(process.StandardOutput);
            var stderr = await ReadAvailableAsync(process.StandardError);

            Assert.AreEqual(string.Empty, stdout, $"mcp-server wrote unexpected stdout before protocol traffic: {stdout}");
            Assert.IsFalse(stderr.Contains("Microsoft.Hosting.Lifetime", StringComparison.Ordinal), stderr);
            Assert.IsFalse(stderr.Contains("ModelContextProtocol.Server.StdioServerTransport", StringComparison.Ordinal), stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeMcpRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }

    private static async Task<string> ReadAvailableAsync(StreamReader reader)
    {
        await Task.Delay(50);
        var builder = new System.Text.StringBuilder();
        while (reader.Peek() >= 0)
        {
            builder.Append((char)reader.Read());
        }

        return builder.ToString();
    }
}
