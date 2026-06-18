using System.Threading;
using UnityEditor;

namespace UnityMcp.AgentBridge.Mcp
{
    [InitializeOnLoad]
    public static class McpEditorLifecycle
    {
        private static CancellationTokenSource _processCts;
        private static CancellationTokenSource _assemblyCts;

        static McpEditorLifecycle()
        {
            _processCts = new CancellationTokenSource();
            _assemblyCts = new CancellationTokenSource();
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public static CancellationToken ProcessToken => _processCts.Token;

        public static CancellationToken AssemblyToken => _assemblyCts.Token;

        private static void OnEditorQuitting()
        {
            Cancel(_processCts);
            Cancel(_assemblyCts);
        }

        private static void OnBeforeAssemblyReload()
        {
            Cancel(_assemblyCts);
        }

        private static void Cancel(CancellationTokenSource cts)
        {
            if (cts == null || cts.IsCancellationRequested)
            {
                return;
            }

            cts.Cancel();
        }
    }
}
