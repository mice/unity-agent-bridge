using UnityAgentBridge.Cli.Commands;
using UnityAgentBridge.Cli.Diagnostics;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Cli;

internal static class AgentBridgeCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || CommandSpecParser.HasHelpFlag(args))
        {
            CommandContract.WriteHelp(CliJsonWriter.Stdout, "unity-agent-bridge");
            return 0;
        }

        try
        {
            var parsed = CommandSpecParser.Parse(args);
            var bridgeClient = new ExternalBridgeClient();
            var queuePaths = bridgeClient.CreateQueuePaths(parsed.Global.ProjectPath, parsed.Global.QueueRoot);
            var commandId = bridgeClient.CreateCommandId();
            var result = await bridgeClient.ExecuteAsync(
                queuePaths,
                commandId,
                new BridgeCommandSpec(parsed.Spec.Tool, parsed.Spec.TimeoutMs, parsed.Spec.ArgsJson),
                cancellationToken);
            CliJsonWriter.WriteResult(result.RawJson, parsed.Global.OutputFormat);
            if (result.IsUnknownStatus)
            {
                CliJsonWriter.WriteDiagnostic(result.Status);
            }

            return ExitCodeMapper.Map(result.Status);
        }
        catch (CommandValidationException exception)
        {
            CliJsonWriter.WriteDiagnostic(exception.Message);
            return 3;
        }
        catch (BridgeCommandValidationException exception)
        {
            CliJsonWriter.WriteDiagnostic(exception.Message);
            return 3;
        }
        catch (TimeoutException exception)
        {
            CliJsonWriter.WriteDiagnostic(exception.Message);
            return 2;
        }
        catch (DirectoryNotFoundException exception)
        {
            CliJsonWriter.WriteDiagnostic(exception.Message);
            return 6;
        }
        catch (Exception exception)
        {
            CliJsonWriter.WriteDiagnostic(exception.ToString());
            return 6;
        }
    }
}
