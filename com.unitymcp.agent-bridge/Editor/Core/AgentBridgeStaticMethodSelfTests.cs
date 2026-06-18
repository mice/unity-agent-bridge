using System;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    public static class AgentBridgeStaticMethodSelfTests
    {
        public static string LastEchoMessage { get; private set; }

        public static void Reset()
        {
            LastEchoMessage = null;
        }

        public static void SelfTestOk()
        {
            Debug.Log("AgentBridgeStaticMethodSelfTests.SelfTestOk().Done");
        }

        public static void SelfTestMissingDone()
        {
        }

        public static void SelfTestThrow()
        {
            throw new InvalidOperationException("selftest boom");
        }

        public static void SelfTestEcho(AgentBridgeStaticMethodEchoArgs args)
        {
            LastEchoMessage = args.message;
            Debug.Log("AgentBridgeStaticMethodSelfTests.SelfTestEcho().Done");
        }

        public static void RunRoslynSpike(RoslynSpikeArgs args)
        {
            var result = RoslynExecutionSpikeHarness.Run(args ?? new RoslynSpikeArgs());
            Debug.Log("AgentBridgeStaticMethodSelfTests.RunRoslynSpike().Done");
            Debug.Log(JsonConvert.SerializeObject(result, Formatting.None));
        }
    }

    [Serializable]
    public sealed class AgentBridgeStaticMethodEchoArgs : IStaticMethodArgsValidator
    {
        public string message;

        public bool Validate(out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                validationMessage = "parameters.message is required.";
                return false;
            }

            validationMessage = null;
            return true;
        }
    }
}
