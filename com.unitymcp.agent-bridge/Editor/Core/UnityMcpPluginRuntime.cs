using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge
{
    public static class UnityMcpPluginRuntime
    {
        public static UnityMcpPluginDiscoveryResult DiscoverAndRegister(
            AgentToolRegistry registry,
            AgentBridgeSettings settings,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginHostServices hostServices = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            var result = new UnityMcpPluginDiscoveryResult();
            hostServices ??= new UnityMcpPluginHostServices
            {
                Settings = settings,
                Queue = new AgentCommandQueue(paths.ProjectRoot, settings.tempRoot),
                Registry = registry,
                Logger = logger
            };
            hostServices.Settings ??= settings;
            hostServices.Queue ??= new AgentCommandQueue(paths.ProjectRoot, settings.tempRoot);
            hostServices.Registry ??= registry;
            hostServices.Logger ??= logger;
            var registrations = settings.pluginRegistrations ?? new List<UnityMcpPluginRegistration>();
            var builtInNames = new HashSet<string>(registry.ListTools().Select(descriptor => descriptor.Name), StringComparer.Ordinal);
            var pluginNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var registration in registrations.Where(static item => item != null && item.enabled))
            {
                try
                {
                    ProcessRegistration(registration, registry, paths, logger, result, builtInNames, pluginNames, hostServices);
                }
                catch (Exception exception)
                {
                    logger?.Exception("plugin_registration_failed", exception);
                }
            }

            WriteCatalog(paths.PluginCatalogPath, result.Catalog, logger);
            return result;
        }

        private static void ProcessRegistration(
            UnityMcpPluginRegistration registration,
            AgentToolRegistry registry,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginDiscoveryResult result,
            ISet<string> builtInNames,
            ISet<string> pluginNames,
            UnityMcpPluginHostServices hostServices)
        {
            var assembly = ResolveAssembly(registration, paths.ProjectRoot);
            if (assembly == null)
            {
                logger?.Warning("plugin_registration_unresolved", $"Plugin registration could not resolve assembly. kind={registration.kind} assembly={registration.assemblyName} dll={registration.dllPath}");
                return;
            }

            var providerTypes = GetProviderTypes(assembly, registration.providerTypeName);
            foreach (var providerType in providerTypes)
            {
                try
                {
                    var attribute = providerType.GetCustomAttribute<UnityMcpPluginAttribute>();
                    if (attribute == null)
                    {
                        logger?.Warning("plugin_provider_missing_attribute", $"Skipping provider '{providerType.FullName}' because it is missing UnityMcpPluginAttribute.");
                        continue;
                    }

                    var provider = Activator.CreateInstance(providerType) as IUnityMcpToolProvider;
                    if (provider == null)
                    {
                        logger?.Warning("plugin_provider_activate_failed", $"Skipping provider '{providerType.FullName}' because it could not be instantiated.");
                        continue;
                    }

                    var pluginContext = new UnityMcpPluginContext
                    {
                        ProjectRoot = paths.ProjectRoot,
                        AssemblyName = assembly.GetName().Name ?? string.Empty,
                        HostServices = hostServices
                    };

                    var tools = provider.GetTools(pluginContext) ?? Array.Empty<IUnityMcpTool>();
                    foreach (var tool in tools)
                    {
                        RegisterPluginTool(tool, attribute, assembly, registry, paths, logger, result, builtInNames, pluginNames);
                    }
                }
                catch (Exception exception)
                {
                    logger?.Exception("plugin_provider_discover_failed", exception);
                }
            }
        }

        private static void RegisterPluginTool(
            IUnityMcpTool tool,
            UnityMcpPluginAttribute attribute,
            Assembly assembly,
            AgentToolRegistry registry,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginDiscoveryResult result,
            ISet<string> builtInNames,
            ISet<string> pluginNames)
        {
            try
            {
                UnityMcpPluginContractValidator.ValidateTool(tool);
                var bridgeToolName = tool.Descriptor.Name;
                if (builtInNames.Contains(bridgeToolName))
                {
                    logger?.Warning("plugin_tool_conflict_builtin", $"Plugin tool '{bridgeToolName}' conflicts with a built-in tool and was rejected.");
                    return;
                }

                if (!pluginNames.Add(bridgeToolName))
                {
                    logger?.Warning("plugin_tool_conflict_plugin", $"Plugin tool '{bridgeToolName}' conflicts with another plugin tool and was rejected.");
                    return;
                }

                var resolvedSchema = ResolveSchema(tool.InputSchema, assembly, paths.ProjectRoot);
                var adapted = new UnityMcpPluginToolAdapter(tool, paths.ProjectRoot, paths.TempRoot);
                registry.Register(adapted);

                result.Catalog.tools.Add(new UnityMcpPluginCatalogTool
                {
                    pluginId = attribute.PluginId,
                    pluginVersion = attribute.PluginVersion,
                    assemblyName = assembly.GetName().Name ?? string.Empty,
                    bridgeTool = bridgeToolName,
                    mcpName = DeriveMcpToolName(bridgeToolName),
                    title = tool.Descriptor.Title,
                    description = tool.Descriptor.Description,
                    defaultTimeoutMs = tool.Descriptor.DefaultTimeoutMs,
                    allowedRuntimeModes = tool.Descriptor.AllowedRuntimeModes.ToString(),
                    sideEffect = tool.Descriptor.SideEffect.ToString(),
                    mayTriggerDomainReload = tool.Descriptor.MayTriggerDomainReload,
                    inputSchemaJson = resolvedSchema
                });
            }
            catch (Exception exception)
            {
                logger?.Exception("plugin_tool_register_failed", exception);
            }
        }

        private static Assembly ResolveAssembly(UnityMcpPluginRegistration registration, string projectRoot)
        {
            switch (registration.kind)
            {
                case UnityMcpPluginRegistrationKind.AsmdefAssembly:
                    return AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, registration.assemblyName, StringComparison.Ordinal));
                case UnityMcpPluginRegistrationKind.ManagedDll:
                    var normalizedPath = Path.GetFullPath(Path.Combine(projectRoot, registration.dllPath.Replace('/', Path.DirectorySeparatorChar)));
                    return AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly =>
                        {
                            var location = assembly.Location;
                            return !string.IsNullOrWhiteSpace(location) &&
                                   string.Equals(Path.GetFullPath(location), normalizedPath, StringComparison.OrdinalIgnoreCase);
                        });
                default:
                    return null;
            }
        }

        private static IEnumerable<Type> GetProviderTypes(Assembly assembly, string providerTypeName)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).ToArray();
            }

            var providers = types
                .Where(type => type != null && !type.IsAbstract && typeof(IUnityMcpToolProvider).IsAssignableFrom(type))
                .Where(type => string.IsNullOrWhiteSpace(providerTypeName) || string.Equals(type.FullName, providerTypeName, StringComparison.Ordinal))
                .ToArray();

            return providers;
        }

        private static string ResolveSchema(UnityMcpSchemaDeclaration declaration, Assembly assembly, string projectRoot)
        {
            switch (declaration.Kind)
            {
                case UnityMcpSchemaKind.InlineJson:
                    return ValidateSchemaJson(declaration.Value);
                case UnityMcpSchemaKind.AssetPath:
                case UnityMcpSchemaKind.PackagePath:
                    var normalizedPath = PathSafety.Normalize(projectRoot, declaration.Value);
                    var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
                    return ValidateSchemaJson(File.ReadAllText(absolutePath));
                case UnityMcpSchemaKind.EmbeddedResource:
                    using (var stream = assembly.GetManifestResourceStream(declaration.ResourceName))
                    {
                        if (stream == null)
                        {
                            throw new FileNotFoundException($"Embedded schema resource '{declaration.ResourceName}' was not found.", declaration.ResourceName);
                        }

                        using (var reader = new StreamReader(stream))
                        {
                            return ValidateSchemaJson(reader.ReadToEnd());
                        }
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(declaration.Kind), declaration.Kind, "Unsupported schema kind.");
            }
        }

        private static string ValidateSchemaJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Schema JSON is required.", nameof(json));
            }

            var token = JToken.Parse(json);
            if (token.Type != JTokenType.Object)
            {
                throw new ArgumentException("Schema JSON root must be an object.", nameof(json));
            }

            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string DeriveMcpToolName(string bridgeToolName)
        {
            const string unityPrefix = "unity.";
            if (bridgeToolName.StartsWith(unityPrefix, StringComparison.Ordinal))
            {
                return "mcp__unity__" + bridgeToolName.Substring(unityPrefix.Length).Replace('.', '_');
            }

            return "mcp__" + bridgeToolName.Replace('.', '_');
        }

        private static void WriteCatalog(string outputPath, UnityMcpPluginCatalog catalog, FileAgentBridgeLogger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, JsonUtil.SerializeObject(catalog));
            }
            catch (Exception exception)
            {
                logger?.Exception("plugin_catalog_write_failed", exception);
            }
        }
    }

    public sealed class UnityMcpPluginDiscoveryResult
    {
        public UnityMcpPluginCatalog Catalog { get; } = new UnityMcpPluginCatalog();
    }

    public sealed class UnityMcpPluginHostServices
    {
        public AgentBridgeSettings Settings { get; set; }

        public AgentCommandQueue Queue { get; set; }

        public AgentToolRegistry Registry { get; set; }

        public FileAgentBridgeLogger Logger { get; set; }
    }

    internal sealed class UnityMcpPluginToolAdapter : IAgentTool
    {
        private readonly IUnityMcpTool _tool;
        private readonly string _projectRoot;
        private readonly string _tempRoot;

        public UnityMcpPluginToolAdapter(IUnityMcpTool tool, string projectRoot, string tempRoot)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            _tempRoot = string.IsNullOrWhiteSpace(tempRoot) ? "Temp/AgentBridge" : tempRoot;
            Descriptor = new ToolDescriptor
            {
                Name = _tool.Descriptor.Name,
                SchemaVersion = JsonUtil.CurrentSchemaVersion,
                Description = _tool.Descriptor.Description,
                AllowedModes = ConvertModes(_tool.Descriptor.AllowedRuntimeModes),
                SideEffect = ConvertSideEffects(_tool.Descriptor.SideEffect),
                MayTriggerDomainReload = _tool.Descriptor.MayTriggerDomainReload,
                ArgsSchemaPath = null
            };
        }

        public ToolDescriptor Descriptor { get; }

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            var pluginContext = new UnityMcpToolContext
            {
                CommandId = context.Command?.commandId,
                ToolName = context.Command?.tool,
                TimeoutMs = context.Command?.timeoutMs ?? 0,
                RawArgsJson = context.RawArgsJson,
                ProjectRoot = _projectRoot,
                TempRoot = _tempRoot
            };
            var pluginResult = _tool.Execute(pluginContext, new UnityMcpCancellationAdapter(cancellation));
            return ConvertResult(pluginResult);
        }

        private static ToolResult ConvertResult(UnityMcpToolResult result)
        {
            if (result == null)
            {
                return new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Failed,
                    summary = "Plugin tool returned null result.",
                    errors = new List<ToolError>
                    {
                        new ToolError
                        {
                            code = "UNITYMCP_PLUGIN_RESULT_NULL",
                            message = "Plugin tool returned null result."
                        }
                    }
                };
            }

            var status = string.IsNullOrWhiteSpace(result.Status)
                ? (result.Success ? ToolResultStatus.Success : ToolResultStatus.Failed)
                : result.Status;
            var summary = string.IsNullOrWhiteSpace(result.Summary)
                ? (string.Equals(status, ToolResultStatus.Success, StringComparison.Ordinal) ? "Plugin tool completed." : "Plugin tool failed.")
                : result.Summary;
            var warnings = result.Warnings?.Select(static item => new ToolWarning
            {
                code = item?.Code,
                message = item?.Message
            }).ToList() ?? new List<ToolWarning>();
            var metricsObjectJson = NormalizeMetricsObjectJson(result.MetricsObjectJson, warnings);

            return new ToolResult
            {
                success = string.Equals(status, ToolResultStatus.Success, StringComparison.Ordinal),
                status = status,
                summary = summary,
                errors = result.Errors?.Select(static item => new ToolError
                {
                    code = item?.Code,
                    message = item?.Message,
                    file = item?.File,
                    line = item?.Line ?? 0,
                    column = item?.Column ?? 0
                }).ToList() ?? new List<ToolError>(),
                warnings = warnings,
                logs = result.Logs?.Select(static item => new ToolLog
                {
                    level = item?.Level,
                    message = item?.Message,
                    timestamp = item?.Timestamp
                }).ToList() ?? new List<ToolLog>(),
                metricsObjectJson = metricsObjectJson,
                changedFiles = result.ChangedFiles ?? new List<string>(),
                reportPath = result.ReportPath
            };
        }

        private static string NormalizeMetricsObjectJson(string metricsObjectJson, List<ToolWarning> warnings)
        {
            if (string.IsNullOrWhiteSpace(metricsObjectJson))
            {
                return "{}";
            }

            try
            {
                var token = JToken.Parse(metricsObjectJson);
                if (token.Type == JTokenType.Object)
                {
                    return token.ToString(Newtonsoft.Json.Formatting.None);
                }
            }
            catch
            {
            }

            warnings.Add(new ToolWarning
            {
                code = "UNITYMCP_PLUGIN_METRICS_INVALID",
                message = "Plugin metricsObjectJson must be a JSON object. The value was replaced with an empty object."
            });
            return "{}";
        }

        private static ToolExecutionModes ConvertModes(UnityMcpToolRuntimeModes modes)
        {
            var converted = ToolExecutionModes.None;
            if ((modes & UnityMcpToolRuntimeModes.Edit) != 0)
            {
                converted |= ToolExecutionModes.Edit;
            }

            if ((modes & UnityMcpToolRuntimeModes.Play) != 0)
            {
                converted |= ToolExecutionModes.Play;
            }

            return converted;
        }

        private static ToolSideEffect ConvertSideEffects(UnityMcpToolSideEffect sideEffect)
        {
            return sideEffect switch
            {
                UnityMcpToolSideEffect.None => ToolSideEffect.None,
                UnityMcpToolSideEffect.ReadsProject => ToolSideEffect.ReadsProject,
                UnityMcpToolSideEffect.MutatesProject => ToolSideEffect.MutatesProject,
                UnityMcpToolSideEffect.RunsUserCode => ToolSideEffect.RunsUserCode,
                _ => ToolSideEffect.None
            };
        }
    }

    internal sealed class UnityMcpCancellationAdapter : IUnityMcpCancellation
    {
        private readonly IAgentCancellation _inner;

        public UnityMcpCancellationAdapter(IAgentCancellation inner)
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
