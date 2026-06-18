using System;
using NUnit.Framework;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class ToolVersionParserTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_019.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_019")]
        public void Parse_StandardSemanticVersion_ReturnsVersion()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse("22.15.0");

            Assert.That(version, Is.EqualTo(new Version(22, 15, 0)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_020.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_020")]
        public void Parse_VersionWithPrefix_ReturnsVersion()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse("v10.0.100");

            Assert.That(version, Is.EqualTo(new Version(10, 0, 100)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_021.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_021")]
        public void Parse_TextAroundVersion_ReturnsFirstMatch()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse("codex-cli 0.34.1-beta");

            Assert.That(version, Is.EqualTo(new Version(0, 34, 1)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_022.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_022")]
        public void Parse_FourPartVersion_PreservesRevision()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse("1.2.3.4");

            Assert.That(version, Is.EqualTo(new Version(1, 2, 3, 4)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_023.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_023")]
        public void Parse_NoVersion_ReturnsNull()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse("missing");

            Assert.That(version, Is.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_024.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_024")]
        public void Parse_EmptyValue_ReturnsNull()
        {
            var parser = new ToolVersionParser();

            var version = parser.Parse(string.Empty);

            Assert.That(version, Is.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_025.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_025")]
        public void MeetsMinimumVersion_EqualVersion_ReturnsTrue()
        {
            var parser = new ToolVersionParser();

            var result = parser.MeetsMinimumVersion("22.15.0", new Version(22, 15, 0));

            Assert.That(result, Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_026.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_026")]
        public void MeetsMinimumVersion_LowerVersion_ReturnsFalse()
        {
            var parser = new ToolVersionParser();

            var result = parser.MeetsMinimumVersion("9.9.9", new Version(10, 0, 100));

            Assert.That(result, Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_027.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_027")]
        public void MeetsMinimumVersion_InvalidVersion_ReturnsFalse()
        {
            var parser = new ToolVersionParser();

            var result = parser.MeetsMinimumVersion("not-a-version", new Version(10, 0, 0));

            Assert.That(result, Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_028.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_028")]
        public void MeetsMinimumVersion_NullMinimum_Throws()
        {
            var parser = new ToolVersionParser();

            Assert.Throws<ArgumentNullException>(() => parser.MeetsMinimumVersion("1.0.0", null));
        }
    }
}
