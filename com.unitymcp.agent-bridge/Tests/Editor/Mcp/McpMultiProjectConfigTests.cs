using System;
using System.IO;
using NUnit.Framework;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpMultiProjectConfigTests
    {
        [Serializable]
        private sealed class ServerConfig
        {
            public string cwd;
            public ServerEnvironment env;
        }
        [Serializable]
        private sealed class ServerEnvironment
        {
            public string UNITY_AGENT_BRIDGE_WORKSPACE_PATH;
            public string UNITY_AGENT_BRIDGE_PROJECT_PATH;
            public string UNITY_AGENT_BRIDGE_ROUTING_MODE;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_268.md
        [TestCase(false)]
        [TestCase(true)]
        [Category("AGBM_268")]
        public void GeneratedConfig_PreservesWorkspaceSeparatelyFromProject(bool launcher)
        {
            var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Workspace With Spaces"));
            var project = Path.Combine(workspace, "Unity");
            var json = McpMultiProjectConfig.BuildJson("C:/Tools/host", project, workspace, launcher);
            var toml = McpMultiProjectConfig.BuildToml("C:/Tools/host", project, workspace, launcher);
            var parsed = UnityEngine.JsonUtility.FromJson<ServerConfig>(json);
            Assert.That(parsed.cwd, Is.EqualTo(workspace));
            Assert.That(parsed.env.UNITY_AGENT_BRIDGE_WORKSPACE_PATH, Is.EqualTo(workspace));
            Assert.That(parsed.env.UNITY_AGENT_BRIDGE_PROJECT_PATH, Is.EqualTo(project));
            Assert.That(parsed.env.UNITY_AGENT_BRIDGE_ROUTING_MODE, Is.EqualTo("multi-project"));
            Assert.That(json, Does.Contain("UNITY_AGENT_BRIDGE_WORKSPACE_PATH"));
            Assert.That(json, Does.Contain("multi-project"));
            Assert.That(toml, Does.Contain("UNITY_AGENT_BRIDGE_WORKSPACE_PATH"));
            Assert.That(toml, Does.Contain("multi-project"));
            Assert.That(json, Does.Contain(workspace.Replace("\\", "\\\\")));
            Assert.That(toml, Does.Contain(project.Replace("\\", "\\\\")));
            if (!launcher)
            {
                Assert.That(json, Does.Contain("--routing-mode"));
                Assert.That(toml, Does.Contain("--routing-mode"));
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_269.md
        [Test]
        [Category("AGBM_269")]
        public void ManagedMigration_PreservesUnrelatedServersAndSections()
        {
            var root = Path.Combine(Path.GetTempPath(), "AgentBridge.ConfigFixture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "config.toml");
                File.WriteAllText(path, "[mcp_servers.other]\ncommand = \"other\"\n\n[mcp_servers.unity_agent_bridge]\ncommand = \"old\"\n");
                File.AppendAllText(path, "[mcp_servers.unity_agent_bridge.env]\nCUSTOM_SETTING = \"preserved\"\n\n[mcp_servers.unity_agent_bridge.permissions]\nallow = [\"read\"]\n");
                var result = new CodexTomlConfigEditor().ApplyManagedBlock(path,
                    McpMultiProjectConfig.BuildToml("host.exe", root, root, false));
                Assert.That(result.Applied, Is.True);
                var content = File.ReadAllText(path);
                Assert.That(content, Does.Contain("[mcp_servers.other]"));
                Assert.That(content, Does.Contain("multi-project"));
                Assert.That(content, Does.Contain("CUSTOM_SETTING = \"preserved\""));
                Assert.That(content, Does.Contain("[mcp_servers.unity_agent_bridge.permissions]"));
                Assert.That(content, Does.Not.Contain("command = \"old\""));
                Assert.That(result.BackupPath, Is.Not.Null.And.Not.Empty);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
