using System;
using System.Collections.Generic;
using UnityMcp.AgentBridge;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.TestRunner
{
    internal sealed class TestRunnerBridgeTool : IUnityMcpTool
    {
        private readonly IAgentTool _inner;
        private readonly AgentBridgeSettings _settings;

        public TestRunnerBridgeTool(IAgentTool inner, AgentBridgeSettings settings, int defaultTimeoutMs, string schemaJson)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Descriptor = new UnityMcpToolDescriptor
            {
                Name = inner.Descriptor.Name,
                Title = GetTitle(inner.Descriptor.Name),
                Description = inner.Descriptor.Description,
                DefaultTimeoutMs = defaultTimeoutMs,
                AllowedRuntimeModes = ConvertModes(inner.Descriptor.AllowedModes),
                SideEffect = ConvertSideEffect(inner.Descriptor.SideEffect),
                MayTriggerDomainReload = inner.Descriptor.MayTriggerDomainReload
            };
            InputSchema = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.InlineJson,
                Value = schemaJson
            };
        }

        public UnityMcpToolDescriptor Descriptor { get; }

        public UnityMcpSchemaDeclaration InputSchema { get; }

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            var command = new AgentCommand
            {
                schemaVersion = JsonUtil.CurrentSchemaVersion,
                commandId = context.CommandId,
                tool = context.ToolName,
                timeoutMs = context.TimeoutMs,
                rawArgsJson = string.IsNullOrWhiteSpace(context.RawArgsJson) ? "{}" : context.RawArgsJson
            };
            var result = _inner.Execute(new AgentToolContext
            {
                Command = command,
                RawArgsJson = command.rawArgsJson,
                Settings = _settings
            }, new AgentCancellationAdapter(cancellation));

            return ConvertResult(result);
        }

        private static UnityMcpToolResult ConvertResult(ToolResult result)
        {
            if (result == null)
            {
                return new UnityMcpToolResult
                {
                    Success = false,
                    Status = UnityMcpToolStatus.Failed,
                    Summary = "Test runner tool returned null result."
                };
            }

            return new UnityMcpToolResult
            {
                Success = result.success,
                Status = result.status,
                Summary = result.summary,
                MetricsObjectJson = string.IsNullOrWhiteSpace(result.metricsObjectJson) ? "{}" : result.metricsObjectJson,
                ReportPath = result.reportPath,
                ChangedFiles = result.changedFiles ?? new List<string>(),
                Errors = ConvertErrors(result.errors),
                Warnings = ConvertWarnings(result.warnings),
                Logs = ConvertLogs(result.logs)
            };
        }

        private static List<UnityMcpToolError> ConvertErrors(IEnumerable<ToolError> errors)
        {
            var converted = new List<UnityMcpToolError>();
            if (errors == null)
            {
                return converted;
            }

            foreach (var error in errors)
            {
                converted.Add(new UnityMcpToolError
                {
                    Code = error?.code,
                    Message = error?.message,
                    File = error?.file,
                    Line = error?.line ?? 0,
                    Column = error?.column ?? 0
                });
            }

            return converted;
        }

        private static List<UnityMcpToolWarning> ConvertWarnings(IEnumerable<ToolWarning> warnings)
        {
            var converted = new List<UnityMcpToolWarning>();
            if (warnings == null)
            {
                return converted;
            }

            foreach (var warning in warnings)
            {
                converted.Add(new UnityMcpToolWarning
                {
                    Code = warning?.code,
                    Message = warning?.message
                });
            }

            return converted;
        }

        private static List<UnityMcpToolLog> ConvertLogs(IEnumerable<ToolLog> logs)
        {
            var converted = new List<UnityMcpToolLog>();
            if (logs == null)
            {
                return converted;
            }

            foreach (var log in logs)
            {
                converted.Add(new UnityMcpToolLog
                {
                    Level = log?.level,
                    Message = log?.message,
                    Timestamp = log?.timestamp
                });
            }

            return converted;
        }

        private static UnityMcpToolRuntimeModes ConvertModes(ToolExecutionModes modes)
        {
            var converted = UnityMcpToolRuntimeModes.None;
            if ((modes & ToolExecutionModes.Edit) != 0)
            {
                converted |= UnityMcpToolRuntimeModes.Edit;
            }

            if ((modes & ToolExecutionModes.Play) != 0)
            {
                converted |= UnityMcpToolRuntimeModes.Play;
            }

            return converted;
        }

        private static UnityMcpToolSideEffect ConvertSideEffect(ToolSideEffect sideEffect)
        {
            return sideEffect switch
            {
                ToolSideEffect.None => UnityMcpToolSideEffect.None,
                ToolSideEffect.ReadsProject => UnityMcpToolSideEffect.ReadsProject,
                ToolSideEffect.MutatesProject => UnityMcpToolSideEffect.MutatesProject,
                ToolSideEffect.RunsUserCode => UnityMcpToolSideEffect.RunsUserCode,
                _ => UnityMcpToolSideEffect.None
            };
        }

        private static string GetTitle(string toolName)
        {
            return toolName switch
            {
                "unity.run_editmode_tests" => "Unity Run EditMode Tests",
                "unity.run_playmode_tests" => "Unity Run PlayMode Tests",
                "unity.agent_bridge_self_test" => "Unity Agent Bridge Self-Test",
                _ => toolName
            };
        }

        private sealed class AgentCancellationAdapter : IAgentCancellation
        {
            private readonly IUnityMcpCancellation _inner;

            public AgentCancellationAdapter(IUnityMcpCancellation inner)
            {
                _inner = inner;
            }

            public bool IsCancellationRequested => _inner != null && _inner.IsCancellationRequested;

            public void ThrowIfCancellationRequested()
            {
                _inner?.ThrowIfCancellationRequested();
            }
        }
    }
}
