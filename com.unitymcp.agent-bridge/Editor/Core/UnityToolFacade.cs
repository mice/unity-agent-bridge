using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    public sealed class UnityToolFacade : IUnityToolFacade
    {
        private readonly AgentToolRegistry _registry;
        private readonly AgentBridgeSettings _settings;
        private readonly FileAgentBridgeLogger _logger;
        private readonly Func<DateTime> _utcNowProvider;
        private readonly IUnityRuntimeModeProvider _runtimeModeProvider;

        public UnityToolFacade(
            AgentToolRegistry registry,
            AgentBridgeSettings settings,
            FileAgentBridgeLogger logger = null,
            Func<DateTime> utcNowProvider = null,
            IUnityRuntimeModeProvider runtimeModeProvider = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
            _runtimeModeProvider = runtimeModeProvider ?? UnityEditorRuntimeModeProvider.Instance;
        }

        public ToolResult Execute(AgentCommand command, IAgentCancellation cancellation)
        {
            var startedAt = _utcNowProvider().ToUniversalTime();
            if (command == null)
            {
                return FinalizeResult(null, ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_NULL", "Command is required."), startedAt, startedAt, false);
            }

            if (command.timeoutMs <= 0 || _settings.maxToolDurationMs <= 0)
            {
                return FinalizeResult(command, ToolResult.InvalidArgs("AGENTBRIDGE_TIMEOUT_INVALID", "Command and settings timeout values must be greater than 0."), startedAt, _utcNowProvider(), false);
            }

            if (!_registry.TryGetTool(command.tool, out var tool))
            {
                return FinalizeResult(command, ToolResult.Unsupported("AGENTBRIDGE_TOOL_UNSUPPORTED", $"Tool '{command.tool}' is not registered."), startedAt, _utcNowProvider(), false);
            }

            var currentMode = _runtimeModeProvider.GetCurrentMode();
            if (!IsAllowedInMode(tool.Descriptor.AllowedModes, currentMode))
            {
                return FinalizeResult(command, BuildModeBlockedResult(tool.Descriptor, currentMode), startedAt, _utcNowProvider(), false);
            }

            var effectiveSettings = ResolveEffectiveSettings();
            var effectiveTimeoutMs = Math.Min(command.timeoutMs, effectiveSettings.maxToolDurationMs);
            var timeoutTruncated = effectiveTimeoutMs != command.timeoutMs;
            var effectiveCancellation = new DeadlineAgentCancellation(startedAt, effectiveTimeoutMs, cancellation, _utcNowProvider);

            try
            {
                effectiveCancellation.ThrowIfCancellationRequested();
                var context = new AgentToolContext
                {
                    Command = command,
                    RawArgsJson = command.rawArgsJson,
                    Settings = effectiveSettings
                };
                var result = tool.Execute(context, effectiveCancellation) ?? new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Failed,
                    summary = "Tool returned null ToolResult."
                };

                var finishedAt = _utcNowProvider().ToUniversalTime();
                if (effectiveCancellation.IsTimedOut || (finishedAt - startedAt).TotalMilliseconds > effectiveTimeoutMs)
                {
                    result = new ToolResult
                    {
                        success = false,
                        status = ToolResultStatus.Timeout,
                        summary = "Tool execution exceeded effective timeout."
                    };
                }

                return FinalizeResult(command, result, startedAt, finishedAt, timeoutTruncated);
            }
            catch (OperationCanceledException)
            {
                var status = effectiveCancellation.IsTimedOut ? ToolResultStatus.Timeout : ToolResultStatus.Cancelled;
                var summary = effectiveCancellation.IsTimedOut ? "Tool execution exceeded effective timeout." : "Tool execution was cancelled.";
                return FinalizeResult(command, new ToolResult
                {
                    success = false,
                    status = status,
                    summary = summary
                }, startedAt, _utcNowProvider(), timeoutTruncated);
            }
            catch (Exception exception)
            {
                _logger?.Exception("tool_execute_failed", exception);
                return FinalizeResult(command, new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Exception,
                    summary = exception.Message,
                    errors = new List<ToolError>
                    {
                        new ToolError
                        {
                            code = "AGENTBRIDGE_TOOL_EXCEPTION",
                            message = exception.ToString()
                        }
                    }
                }, startedAt, _utcNowProvider(), timeoutTruncated);
            }
        }

        public IReadOnlyList<ToolDescriptor> ListTools()
        {
            return _registry.ListTools();
        }

        public bool TryGetTool(string toolName, out IAgentTool tool)
        {
            return _registry.TryGetTool(toolName, out tool);
        }

        private ToolResult FinalizeResult(AgentCommand command, ToolResult result, DateTime startedAtUtc, DateTime finishedAtUtc, bool timeoutTruncated)
        {
            result.schemaVersion = result.schemaVersion ?? JsonUtil.CurrentSchemaVersion;
            result.commandId = command?.commandId ?? result.commandId;
            result.tool = command?.tool ?? result.tool;
            result.startedAt = startedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            result.finishedAt = finishedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            result.durationMs = (long)Math.Max(0, (finishedAtUtc - startedAtUtc).TotalMilliseconds);

            if (string.IsNullOrWhiteSpace(result.status))
            {
                result.status = result.success ? ToolResultStatus.Success : ToolResultStatus.Failed;
            }

            result.success = string.Equals(result.status, ToolResultStatus.Success, StringComparison.Ordinal);
            if (timeoutTruncated)
            {
                result.warnings.Add(new ToolWarning
                {
                    code = "AGENTBRIDGE_TIMEOUT_TRUNCATED",
                    message = "effectiveTimeoutMs was truncated by settings.maxToolDurationMs."
                });
            }

            return result;
        }

        private AgentBridgeSettings ResolveEffectiveSettings()
        {
            if (_settings != null)
            {
                return _settings;
            }

            var loadResult = AgentBridgeSettingsLoader.Load();
            if (loadResult?.Settings != null)
            {
                return loadResult.Settings;
            }

            return AgentBridgeSettingsLoader.CreateDefaultSettings();
        }

        private static bool IsAllowedInMode(ToolExecutionModes allowedModes, UnityRuntimeMode currentMode)
        {
            switch (currentMode)
            {
                case UnityRuntimeMode.EditMode:
                    return (allowedModes & ToolExecutionModes.Edit) != 0;
                case UnityRuntimeMode.PlayMode:
                    return (allowedModes & ToolExecutionModes.Play) != 0;
                case UnityRuntimeMode.EnteringPlayMode:
                case UnityRuntimeMode.ExitingPlayMode:
                default:
                    return false;
            }
        }

        private static ToolResult BuildModeBlockedResult(ToolDescriptor descriptor, UnityRuntimeMode currentMode)
        {
            var currentModeLabel = GetRuntimeModeLabel(currentMode);
            var allowedModeLabels = ToolDescriptorDisplay.GetAllowedModeLabels(descriptor.AllowedModes);
            var requiresLabel = ToolDescriptorDisplay.GetAllowedModeSummary(descriptor.AllowedModes);
            var isTransitionMode = currentMode == UnityRuntimeMode.EnteringPlayMode || currentMode == UnityRuntimeMode.ExitingPlayMode;
            var summary = isTransitionMode
                ? $"Tool '{descriptor.Name}' is blocked while Unity is {currentModeLabel.ToLowerInvariant()}. Allowed modes: {requiresLabel}."
                : $"Tool '{descriptor.Name}' requires {requiresLabel}. Current mode: {currentModeLabel}.";

            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Blocked,
                summary = summary,
                errors = new List<ToolError>
                {
                    new ToolError
                    {
                        code = "AGENTBRIDGE_TOOL_MODE_BLOCKED",
                        message = summary
                    }
                },
                metricsObjectJson = JsonUtil.SerializeObject(new ToolModeBlockedMetrics
                {
                    commandName = descriptor.Name,
                    currentMode = currentModeLabel,
                    allowedModes = allowedModeLabels
                })
            };
        }

        private static string GetRuntimeModeLabel(UnityRuntimeMode mode)
        {
            switch (mode)
            {
                case UnityRuntimeMode.EditMode:
                    return "Edit Mode";
                case UnityRuntimeMode.PlayMode:
                    return "Play Mode";
                case UnityRuntimeMode.EnteringPlayMode:
                    return "Entering Play Mode";
                case UnityRuntimeMode.ExitingPlayMode:
                    return "Exiting Play Mode";
                default:
                    return mode.ToString();
            }
        }

        [Serializable]
        private sealed class ToolModeBlockedMetrics
        {
            public string commandName;
            public string currentMode;
            public string[] allowedModes;
        }
    }
}
