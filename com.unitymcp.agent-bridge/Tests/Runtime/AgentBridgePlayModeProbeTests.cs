using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentBridgePlayModeProbeTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_062.md
        [UnityTest]
        [Category("AGB_PlayMode")]
        [Category("AGB_062")]
        public IEnumerator DemoPlayModeProbe_PassesAfterOneFrame()
        {
            yield return null;
            Assert.That(1, Is.EqualTo(1));
        }
    }
}
