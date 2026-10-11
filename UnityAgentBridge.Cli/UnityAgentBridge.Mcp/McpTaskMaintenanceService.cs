using Microsoft.Extensions.Hosting;

namespace UnityAgentBridge.Mcp;

internal sealed class McpTaskMaintenanceService(McpTaskStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await store.MaintainAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { Console.Error.WriteLine("MCP task maintenance: " + exception.Message); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
