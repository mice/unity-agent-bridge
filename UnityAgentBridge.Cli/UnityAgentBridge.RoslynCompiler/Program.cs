using Newtonsoft.Json;

namespace UnityAgentBridge.RoslynCompiler;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                return WriteFailure("invalid_request", "Expected a single compile request JSON path argument.");
            }

            var requestPath = args[0];
            if (!File.Exists(requestPath))
            {
                return WriteFailure("invalid_request", $"Compile request file not found: {requestPath}");
            }

            var request = CompileProtocolJson.DeserializeRequest(File.ReadAllText(requestPath));
            using var cancellationTokenSource = request.TimeoutMs > 0
                ? new CancellationTokenSource(request.TimeoutMs)
                : new CancellationTokenSource();

            var service = new RoslynCompileService();
            var response = service.Compile(request, cancellationTokenSource.Token);
            Console.Out.WriteLine(CompileProtocolJson.SerializeResponse(response));
            return response.Success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            return WriteFailure("timeout", "Compilation timed out.");
        }
        catch (Exception exception)
        {
            return WriteFailure("proxy_failed", exception.Message);
        }
    }

    private static int WriteFailure(string exitStatus, string message)
    {
        var response = new CompileResponse
        {
            Success = false,
            ExitStatus = exitStatus,
            ErrorSummary = message
        };
        Console.Out.WriteLine(JsonConvert.SerializeObject(response, Formatting.None));
        return 1;
    }
}
