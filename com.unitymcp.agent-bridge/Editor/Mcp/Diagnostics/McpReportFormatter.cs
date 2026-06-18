using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.PackageManager;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpReportFormatter
    {
        public string Format(
            IReadOnlyList<McpDiagnosticCheck> checks,
            McpReadiness readiness,
            McpEditorSettings settings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("MCP Diagnostics Report");
            builder.Append("GeneratedAtUtc: ").AppendLine(DateTime.UtcNow.ToString("O"));
            builder.Append("Readiness: ").AppendLine(readiness.ToString());
            builder.Append("UnityVersion: ").AppendLine(Application.unityVersion);
            builder.Append("ProjectPath: ").AppendLine(GetProjectPath());
            builder.Append("McpServerVersion: ").AppendLine(GetMcpServerVersion(settings));
            builder.AppendLine("Checks:");

            if (checks != null)
            {
                for (var index = 0; index < checks.Count; index++)
                {
                    var check = checks[index];
                    builder.Append("- ")
                        .Append(check.Code)
                        .Append(" [")
                        .Append(check.Severity)
                        .Append("] ")
                        .Append(check.Summary)
                        .Append(" (")
                        .Append((int)check.Duration.TotalMilliseconds)
                        .AppendLine("ms)");

                    if (!string.IsNullOrEmpty(check.Details))
                    {
                        builder.Append("  Details: ").AppendLine(Redact(check.Details));
                    }

                    if (!string.IsNullOrEmpty(check.Remediation))
                    {
                        builder.Append("  Remediation: ").AppendLine(Redact(check.Remediation));
                    }
                }
            }

            return builder.ToString();
        }

        private static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (ContainsSensitiveLabel(value))
            {
                return "[redacted]";
            }

            return value;
        }

        private static bool ContainsSensitiveLabel(string value)
        {
            return value.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("bearer ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetProjectPath()
        {
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath);
            return projectRoot != null ? projectRoot.FullName : "Unknown";
        }

        private static string GetMcpServerVersion(McpEditorSettings settings)
        {
            try
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(McpReportFormatter).Assembly);
                if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.version))
                {
                    return "Unknown";
                }

                return packageInfo.version;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
