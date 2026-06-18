using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class ToolError
    {
        public string code;
        public string message;
        public string file;
        public int line;
        public int column;
    }
}
