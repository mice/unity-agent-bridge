using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    internal static class EditorBasicsResult
    {
        public static UnityMcpToolResult InvalidArgs(string code, string message)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = UnityMcpToolStatus.InvalidArgs,
                Summary = message,
                Errors = new List<UnityMcpToolError>
                {
                    new UnityMcpToolError
                    {
                        Code = code,
                        Message = message
                    }
                }
            };
        }
    }
}
