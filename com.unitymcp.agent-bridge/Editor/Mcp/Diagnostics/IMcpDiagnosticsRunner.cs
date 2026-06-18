using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public interface IMcpDiagnosticsRunner
    {
        Task<IReadOnlyList<McpDiagnosticCheck>> RunAsync(
            McpEditorSettings settings,
            CancellationToken cancellationToken);
    }
}
