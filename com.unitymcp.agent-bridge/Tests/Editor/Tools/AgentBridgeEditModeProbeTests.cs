using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentBridgeEditModeProbeTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_061.md
        [Test]
        [Category("AGB_EditMode")]
        [Category("AGB_061")]
        public void DemoEditModeProbe_Passes()
        {
            Assert.That(2 + 2, Is.EqualTo(4));
        }
    }
}
