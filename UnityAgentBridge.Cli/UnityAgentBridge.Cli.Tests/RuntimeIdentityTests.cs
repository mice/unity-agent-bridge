using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class RuntimeIdentityTests
{
    [TestMethod]
    public void RuntimeIdentity_ExposesFrozenProtocolVersion()
    {
        Assert.AreEqual("1.0", RuntimeIdentity.ProtocolVersion);
    }

    [TestMethod]
    public void RuntimeIdentity_ExposesSemanticRuntimeVersion()
    {
        StringAssert.Matches(RuntimeIdentity.RuntimeVersion, new System.Text.RegularExpressions.Regex(@"^\d+\.\d+\.\d+"));
        Assert.AreEqual(RuntimeIdentity.RuntimeVersion, RuntimeIdentity.PackageVersion);
    }
}
