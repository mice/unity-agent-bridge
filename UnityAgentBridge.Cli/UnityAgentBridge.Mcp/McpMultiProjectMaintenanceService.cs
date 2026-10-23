using Microsoft.Extensions.Hosting;

namespace UnityAgentBridge.Mcp;

internal sealed class McpMultiProjectMaintenanceService(McpMultiProjectHost host) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await host.MaintainAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { Console.Error.WriteLine("MCP project task maintenance: " + e.Message); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
