using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskEditor;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class RunPlayModeTestsTaskContractTests
    {
        private TestRunnerApi api;

        [SetUp]
        public void SetUp() => api = ScriptableObject.CreateInstance<TestRunnerApi>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(api);

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_234.md
        [Test]
        [Category("AGB_234")]
        public void Constructor_RejectsEditModeFilter()
        {
            var filter = new Filter { testMode = TestMode.EditMode };
            Assert.Throws<System.ArgumentException>(() =>
                new RunPlayModeTestsTask(api, filter));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_235.md
        [Test]
        [Category("AGB_235")]
        public void AdvanceBeforeStart_RejectsInvalidLifecycle()
        {
            var task = new RunPlayModeTestsTask(
                api,
                new Filter { testMode = TestMode.PlayMode });
            Assert.Throws<System.InvalidOperationException>(() => task.Advance(null));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_236.md
        [Test]
        [Category("AGB_236")]
        public void Cancellation_IsExplicitlyUnsupported()
        {
            var task = new RunPlayModeTestsTask(
                api,
                new Filter { testMode = TestMode.PlayMode });
            Assert.Throws<System.NotSupportedException>(() => task.RequestCancellation(null));
        }
    }
}
