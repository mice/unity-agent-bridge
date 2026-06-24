using UnityAgentBridge.Cli;
using UnityAgentBridge.Mcp;

if (args.Length > 0 && string.Equals(args[0], "mcp-server", StringComparison.Ordinal))
{
    await McpServerRuntime.RunAsync(CancellationToken.None);
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "mcp-probe", StringComparison.Ordinal))
{
    Console.WriteLine(await McpProbeRunner.RunAsync(CancellationToken.None));
    return 0;
}

return await AgentBridgeCli.RunAsync(args, CancellationToken.None);
