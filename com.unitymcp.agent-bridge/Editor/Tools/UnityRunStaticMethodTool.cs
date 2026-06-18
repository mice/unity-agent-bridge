using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.run_static_method")]
    public sealed class UnityRunStaticMethodTool : IAgentTool
    {
        private readonly IAssetDatabaseOps _assetDatabaseOps;
        private readonly Func<DateTime> _utcNowProvider;

        public UnityRunStaticMethodTool()
            : this(new UnityAssetDatabaseOps(), () => DateTime.UtcNow)
        {
        }

        internal UnityRunStaticMethodTool(IAssetDatabaseOps assetDatabaseOps, Func<DateTime> utcNowProvider)
        {
            _assetDatabaseOps = assetDatabaseOps ?? throw new ArgumentNullException(nameof(assetDatabaseOps));
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.run_static_method",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run an allowlisted public static Unity method.",
            AllowedModes = ToolExecutionModes.EditAndPlay,
            SideEffect = ToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = true,
            ArgsSchemaPath = "Documentation~/schemas/unity.run_static_method.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();

            if (!TryParseArgs(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            var settings = context.Settings;
            if (settings == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_SETTINGS_NULL", "Settings are required.");
            }

            var entry = settings.allowedStaticMethods?.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.typeName, args.typeName, StringComparison.Ordinal) &&
                string.Equals(item.methodName, args.methodName, StringComparison.Ordinal));

            if (entry == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_NOT_ALLOWED", "Requested static method is not in the whitelist.");
            }

            if (entry.maxDurationMs <= 0 || settings.maxToolDurationMs <= 0 || context.Command.timeoutMs <= 0)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_TIMEOUT_INVALID", "Command, whitelist and settings timeout values must be greater than 0.");
            }

            if (!string.Equals(entry.sideEffects, "read", StringComparison.Ordinal) &&
                !string.Equals(entry.sideEffects, "validate", StringComparison.Ordinal))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_SIDE_EFFECT_INVALID", "Whitelist entry sideEffects must be read or validate in V1.");
            }

            var effectiveTimeoutMs = Math.Min(context.Command.timeoutMs, Math.Min(entry.maxDurationMs, settings.maxToolDurationMs));
            var startedAtUtc = _utcNowProvider().ToUniversalTime();
            var warnings = new List<ToolWarning>();
            if (effectiveTimeoutMs < Math.Min(context.Command.timeoutMs, settings.maxToolDurationMs))
            {
                warnings.Add(new ToolWarning
                {
                    code = "AGENTBRIDGE_TIMEOUT_TRUNCATED",
                    message = "effectiveTimeoutMs was truncated by whitelist.maxDurationMs."
                });
            }

            try
            {
                var method = ResolveMethod(entry, out var resolveFailure);
                if (method == null)
                {
                    return resolveFailure;
                }

                var invocationParameters = BuildInvocationParameters(method, entry, args.rawParametersJson, out var parameterFailure);
                if (parameterFailure != null)
                {
                    return parameterFailure;
                }

                cancellation?.ThrowIfCancellationRequested();
                if (( _utcNowProvider().ToUniversalTime() - startedAtUtc).TotalMilliseconds > effectiveTimeoutMs)
                {
                    return BuildTimeoutResult(warnings);
                }

                if (entry.allowAssetDatabaseSaveAssets)
                {
                    _assetDatabaseOps.SaveAssets();
                }

                var logSequenceBefore = AgentConsoleLogStore.GetLatestSequence();
                method.Invoke(null, invocationParameters);

                if (entry.allowAssetDatabaseRefresh)
                {
                    _assetDatabaseOps.Refresh();
                }

                cancellation?.ThrowIfCancellationRequested();
                if ((_utcNowProvider().ToUniversalTime() - startedAtUtc).TotalMilliseconds > effectiveTimeoutMs)
                {
                    return BuildTimeoutResult(warnings);
                }

                if (!string.IsNullOrWhiteSpace(entry.doneLogPattern) &&
                    !AgentConsoleLogStore.ContainsMessageSince(logSequenceBefore, entry.doneLogPattern))
                {
                    warnings.Add(new ToolWarning
                    {
                        code = "AGENTBRIDGE_DONE_MARK_MISSING",
                        message = "Static method returned without emitting the configured done log pattern."
                    });
                }

                var metrics = new RunStaticMethodMetrics
                {
                    whitelistId = entry.id,
                    typeName = entry.typeName,
                    methodName = entry.methodName,
                    hadParameters = invocationParameters.Length == 1
                };

                var result = new ToolResult
                {
                    success = true,
                    status = ToolResultStatus.Success,
                    summary = "Static method executed.",
                    warnings = warnings,
                    metricsObjectJson = JsonUtil.SerializeObject(metrics)
                };
                result.reportPath = AgentBridgeReportWriter.WriteReport(settings, context.Command.commandId, "run_static_method", metrics);
                return result;
            }
            catch (TargetInvocationException exception)
            {
                return BuildExceptionResult(exception.InnerException ?? exception, warnings);
            }
            catch (OperationCanceledException)
            {
                return BuildTimeoutResult(warnings);
            }
            catch (Exception exception)
            {
                return BuildExceptionResult(exception, warnings);
            }
        }

        private static bool TryParseArgs(string rawArgsJson, out RunStaticMethodArgs args, out ToolResult failure)
        {
            args = null;
            if (!JsonUtil.TryReadTopLevelObject(rawArgsJson, out var properties, out failure))
            {
                return false;
            }

            if (!JsonUtil.TryReadStringProperty(properties, "typeName", true, out var typeName, out failure) ||
                !JsonUtil.TryReadStringProperty(properties, "methodName", true, out var methodName, out failure))
            {
                return false;
            }

            if (!JsonUtil.TryReadObjectProperty(properties, "parameters", false, out var rawParametersJson, out failure))
            {
                return false;
            }

            args = new RunStaticMethodArgs
            {
                typeName = typeName,
                methodName = methodName,
                rawParametersJson = string.IsNullOrWhiteSpace(rawParametersJson) ? "{}" : rawParametersJson
            };
            return true;
        }

        private static MethodInfo ResolveMethod(AllowedStaticMethodEntry entry, out ToolResult failure)
        {
            failure = null;
            var targetType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(entry.typeName, false))
                .FirstOrDefault(type => type != null);

            if (targetType == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_TYPE_NOT_FOUND", $"Type '{entry.typeName}' could not be resolved.");
                return null;
            }

            var method = targetType.GetMethod(entry.methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_NOT_FOUND", $"Method '{entry.methodName}' could not be resolved.");
                return null;
            }

            if (!method.IsStatic || !method.IsPublic)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_SIGNATURE_INVALID", "Method must be public static.");
                return null;
            }

            return method;
        }

        private static object[] BuildInvocationParameters(MethodInfo method, AllowedStaticMethodEntry entry, string rawParametersJson, out ToolResult failure)
        {
            failure = null;
            var parameters = method.GetParameters();
            var expectsParameter = !string.IsNullOrWhiteSpace(entry.parameterDtoTypeName);

            if (!expectsParameter)
            {
                if (parameters.Length != 0)
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_SIGNATURE_INVALID", "Whitelist entry does not declare parameters, but target method expects them.");
                    return null;
                }

                return Array.Empty<object>();
            }

            if (parameters.Length != 1)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_SIGNATURE_INVALID", "Parameterized whitelist entries must target a method with exactly one parameter.");
                return null;
            }

            var dtoType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(entry.parameterDtoTypeName, false))
                .FirstOrDefault(type => type != null);
            if (dtoType == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_PARAMETER_TYPE_NOT_FOUND", $"Parameter DTO '{entry.parameterDtoTypeName}' could not be resolved.");
                return null;
            }

            if (parameters[0].ParameterType != dtoType)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_STATIC_METHOD_SIGNATURE_INVALID", "Target method parameter type does not match whitelist parameterDtoTypeName.");
                return null;
            }

            if (!JsonUtil.TryReadTopLevelObject(rawParametersJson, out _, out failure))
            {
                return null;
            }

            object dtoInstance;
            try
            {
                dtoInstance = JsonUtility.FromJson(rawParametersJson, dtoType);
            }
            catch (Exception exception)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return null;
            }

            if (dtoInstance == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", "parameters could not be deserialized.");
                return null;
            }

            if (dtoInstance is IStaticMethodArgsValidator validator && !validator.Validate(out var validationMessage))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_SCHEMA_VALIDATION_FAILED", validationMessage ?? "parameters failed validation.");
                return null;
            }

            return new[] { dtoInstance };
        }

        private static ToolResult BuildTimeoutResult(List<ToolWarning> warnings)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Timeout,
                summary = "Static method execution exceeded effective timeout.",
                warnings = warnings ?? new List<ToolWarning>()
            };
        }

        private static ToolResult BuildExceptionResult(Exception exception, List<ToolWarning> warnings)
        {
            var result = new ToolResult
            {
                success = false,
                status = ToolResultStatus.Exception,
                summary = exception?.Message ?? "Static method execution failed.",
                warnings = warnings ?? new List<ToolWarning>()
            };
            result.errors.Add(new ToolError
            {
                code = "AGENTBRIDGE_STATIC_METHOD_EXCEPTION",
                message = exception == null ? "unknown exception" : exception.ToString()
            });
            return result;
        }
    }

    [Serializable]
    public sealed class RunStaticMethodArgs
    {
        public string typeName;
        public string methodName;
        public string rawParametersJson;
    }

    [Serializable]
    public sealed class RunStaticMethodMetrics
    {
        public string whitelistId;
        public string typeName;
        public string methodName;
        public bool hadParameters;
    }

    public interface IStaticMethodArgsValidator
    {
        bool Validate(out string validationMessage);
    }

    internal interface IAssetDatabaseOps
    {
        void Refresh();
        void SaveAssets();
    }

    internal sealed class UnityAssetDatabaseOps : IAssetDatabaseOps
    {
        public void Refresh()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public void SaveAssets()
        {
            AssetDatabase.SaveAssets();
        }
    }
}
