using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PathSafetyTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(_projectRoot, Is.Not.Null.And.Not.Empty);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_004.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_004")]
        public void Normalize_AssetsPath_ReturnsCanonicalRelativePath()
        {
            var normalized = PathSafety.Normalize(_projectRoot, "Assets/Scenes/AppMain.unity");

            Assert.That(normalized, Is.EqualTo("Assets/Scenes/AppMain.unity"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_005.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_005")]
        public void Normalize_PackagesPath_ReturnsCanonicalRelativePath()
        {
            var normalized = PathSafety.Normalize(_projectRoot, "Packages/com.unity.test-framework/package.json");

            Assert.That(normalized, Is.EqualTo("Packages/com.unity.test-framework/package.json"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_006.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_006")]
        public void Normalize_RejectsParentTraversalSegment()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, "Assets/../Secrets.txt"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_007.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_007")]
        public void Normalize_RejectsBareParentTraversal()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, ".."));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_008.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_008")]
        public void Normalize_RejectsWindowsAbsolutePath()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, "C:/Windows/System32/cmd.exe"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_009.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_009")]
        public void Normalize_RejectsUnixAbsolutePath()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, "/etc/passwd"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_010.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_010")]
        public void Normalize_RejectsInvalidPrefix()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, "Documentation~/foo.md"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_011.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_011")]
        public void Normalize_RejectsEmptyPath()
        {
            Assert.Throws<ArgumentException>(() => PathSafety.Normalize(_projectRoot, string.Empty));
        }
    }
}
