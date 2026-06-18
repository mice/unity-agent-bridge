using System.Collections.Generic;
using UnityMcp.AgentBridge;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.TestRunner
{
    [UnityMcpPlugin("com.unitymcp.builtin.test-runner", "1.0.0")]
    public sealed class TestRunnerProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            var hostServices = (UnityMcpPluginHostServices)context.HostServices;
            return new IUnityMcpTool[]
            {
                new TestRunnerBridgeTool(new UnityEditModeTestTool(), hostServices.Settings, 120000, TestRunnerSchemas.RunEditModeTests),
                new TestRunnerBridgeTool(new UnityPlayModeTestTool(), hostServices.Settings, 180000, TestRunnerSchemas.RunPlayModeTests),
                new TestRunnerBridgeTool(new UnitySelfTestTool(new AgentBridgeSelfTestRunner(
                    new UnityToolFacade(hostServices.Registry, hostServices.Settings, hostServices.Logger),
                    hostServices.Queue,
                    hostServices.Settings,
                    hostServices.Logger)), hostServices.Settings, 120000, TestRunnerSchemas.AgentBridgeSelfTest)
            };
        }
    }
}
