using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge
{
    public static class UnityMcpPluginRuntime
    {
        private static Func<string, UnityMcpRoslynCompilerPayload> _roslynCompilerPayloadResolver;
        private static readonly AgentPluginLifetimeCache<IUnityMcpToolProvider> ManifestProviders =
            new AgentPluginLifetimeCache<IUnityMcpToolProvider>(exception => Debug.LogWarning("Plugin retirement failed: " + exception.Message));
        private static readonly Dictionary<string, IUnityMcpToolProvider> ConfigurationProviders = new Dictionary<string, IUnityMcpToolProvider>(StringComparer.Ordinal);
        private static AgentBridgeSettings _lastDiscoverySettings;
        private static IReadOnlyList<UnityMcpInstalledPlugin> _lastInstalledPlugins = Array.Empty<UnityMcpInstalledPlugin>();

        static UnityMcpPluginRuntime()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RetireAllProviders;
            EditorApplication.quitting += RetireAllProviders;
        }

        private static void RetireAllProviders()
        {
            ManifestProviders.RetainOnly(Array.Empty<string>());
            ConfigurationProviders.Clear();
            _lastDiscoverySettings = null;
            _lastInstalledPlugins = Array.Empty<UnityMcpInstalledPlugin>();
        }

        public static bool CanConfigure(string pluginId) => !string.IsNullOrWhiteSpace(pluginId) && ConfigurationProviders.TryGetValue(pluginId, out var provider) && provider is IAgentPluginConfiguration;

        public static void OpenConfiguration(string pluginId)
        {
            if (ConfigurationProviders.TryGetValue(pluginId, out var provider) && provider is IAgentPluginConfiguration configuration)
                configuration.OpenConfiguration();
        }

        private static string InstallationIdentity(UnityMcpInstalledPlugin plugin, string projectRoot)
            => string.Join("|", projectRoot, plugin.PluginId, plugin.Version, plugin.PackageRoot,
                plugin.Assembly.FullName, plugin.ProviderType.FullName);

        public static void ConfigureRoslynCompilerPayloadResolver(Func<string, UnityMcpRoslynCompilerPayload> resolver)
        {
            _roslynCompilerPayloadResolver = resolver;
        }

        public static UnityMcpPluginDiscoveryResult DiscoverAndRegister(
            AgentToolRegistry registry,
            AgentBridgeSettings settings,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginHostServices hostServices = null,
            bool publishCatalog = true)
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
            hostServices.RoslynCompilerPayload ??= ResolveRoslynCompilerPayload(paths);
            var registrations = settings.pluginRegistrations ?? new List<UnityMcpPluginRegistration>();
            var builtInNames = new HashSet<string>(registry.ListTools().Select(descriptor => descriptor.Name), StringComparer.Ordinal);
            var pluginNames = new HashSet<string>(StringComparer.Ordinal);
            var pluginMcpNames = new HashSet<string>(
                registry.ListTools()
                    .Select(descriptor => McpToolNameMapper.TryToCanonicalMcpName(descriptor.Name, out var name) ? name : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            foreach (var registration in registrations.Where(static item => item != null && item.enabled))
            {
                try
                {
                    ProcessRegistration(registration, registry, paths, logger, result, builtInNames, pluginNames, pluginMcpNames, hostServices);
                }
                catch (Exception exception)
                {
                    logger?.Exception("plugin_registration_failed", exception);
                }
            }

            var installedPlugins = UnityMcpPluginManifestDiscovery.Discover(settings);
            result.InstalledPlugins.AddRange(installedPlugins);
            var candidates = installedPlugins.Where(item => item.Enabled && item.Ready).ToArray();
            ManifestProviders.RetainOnly(candidates.Select(item => InstallationIdentity(item, paths.ProjectRoot)));
            ManifestProviders.BeginRefresh();
            ConfigurationProviders.Clear();
            foreach (var plugin in candidates)
            {
                var catalogStart = result.Catalog.tools.Count;
                var registryBefore = new HashSet<string>(registry.ListTools().Select(item => item.Name), StringComparer.Ordinal);
                try
                {
                    ProcessManifestPlugin(plugin, registry, paths, logger, result, builtInNames, pluginNames, pluginMcpNames, hostServices);
                }
                catch (Exception exception)
                {
                    ManifestProviders.Reject(InstallationIdentity(plugin, paths.ProjectRoot));
                    foreach (var added in registry.ListTools().Where(item => !registryBefore.Contains(item.Name)).ToArray())
                    {
                        registry.Unregister(added.Name);
                        pluginNames.Remove(added.Name);
                    }
                    foreach (var added in result.Catalog.tools.Skip(catalogStart)) pluginMcpNames.Remove(added.mcpName);
                    result.Catalog.tools.RemoveRange(catalogStart, result.Catalog.tools.Count - catalogStart);
                    plugin.Ready = false;
                    plugin.Exposed = false;
                    plugin.DiagnosticCode = UnityMcpPluginDiagnosticCodes.ProviderInvalid;
                    plugin.DiagnosticMessage = exception.Message;
                    logger?.Exception("plugin_manifest_provider_failed", exception);
                }
            }
            ManifestProviders.EndRefresh();

            if (publishCatalog) result.CapabilityRevision = WriteCatalog(paths.PluginCatalogPath, result.Catalog, logger);
            _lastDiscoverySettings = settings;
            _lastInstalledPlugins = result.InstalledPlugins.ToArray();
            return result;
        }

        public static IReadOnlyList<UnityMcpInstalledPlugin> DiscoverInstalledPlugins(AgentBridgeSettings settings)
        {
            var installed = UnityMcpPluginManifestDiscovery.Discover(settings);
            if (!ReferenceEquals(settings, _lastDiscoverySettings)) return installed;
            foreach (var plugin in installed)
            {
                var previous = _lastInstalledPlugins.FirstOrDefault(item => item.PluginId == plugin.PluginId &&
                    item.Version == plugin.Version && item.PackageRoot == plugin.PackageRoot && item.Enabled == plugin.Enabled);
                if (previous == null || !plugin.Ready) continue;
                plugin.Ready = previous.Ready;
                plugin.Exposed = previous.Exposed;
                plugin.DiagnosticCode = previous.DiagnosticCode;
                plugin.DiagnosticMessage = previous.DiagnosticMessage;
            }
            return installed;
        }

        private static UnityMcpRoslynCompilerPayload ResolveRoslynCompilerPayload(AgentBridgePaths paths)
        {
            var resolver = _roslynCompilerPayloadResolver;
            if (resolver != null)
            {
                try
                {
                    var payload = resolver(paths.ProjectRoot);
                    if (payload != null)
                    {
                        return payload;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Roslyn compiler payload resolution failed: " + exception.Message);
                }
            }

            var runtimeMode = "project-local";
            var runtimeVersion = string.Empty;
            var machineRuntimeRoot = string.Empty;
            var settingsPath = Path.Combine(paths.ProjectRoot, "Library", "AgentBridge", "mcp-editor-settings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    var settings = JObject.Parse(File.ReadAllText(settingsPath));
                    runtimeMode = string.Equals(settings.Value<string>("runtimeMode"), "machine", StringComparison.OrdinalIgnoreCase)
                        ? "machine"
                        : "project-local";
                    runtimeVersion = settings.Value<string>("runtimeVersion") ?? string.Empty;
                    machineRuntimeRoot = settings.Value<string>("machineRuntimeRoot") ?? string.Empty;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Roslyn compiler payload fallback settings could not be read: " + exception.Message);
                }
            }

            return new UnityMcpRoslynCompilerPayload
            {
                RuntimeMode = runtimeMode,
                RuntimeVersion = runtimeVersion,
                CompilerPath = string.Equals(runtimeMode, "machine", StringComparison.Ordinal)
                    ? ResolveMachineRoslynCompilerPath(machineRuntimeRoot, runtimeVersion)
                    : Path.Combine(
                        paths.ProjectRoot,
                        ".unitymcp",
                        "runtime",
                        "UnityAgentBridge",
                        "roslyn-execution",
                        "out",
                        "win-x64",
                        "unity-roslyn-compiler.exe")
            };
        }

        private static string ResolveMachineRoslynCompilerPath(string machineRuntimeRoot, string runtimeVersion)
        {
            if (string.IsNullOrWhiteSpace(machineRuntimeRoot) || string.IsNullOrWhiteSpace(runtimeVersion))
            {
                return string.Empty;
            }

            return Path.Combine(
                machineRuntimeRoot.Trim(),
                "versions",
                runtimeVersion.Trim(),
                "runtime",
                "win-x64",
                "unity-roslyn-compiler.exe");
        }

        private static void ProcessRegistration(
            UnityMcpPluginRegistration registration,
            AgentToolRegistry registry,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginDiscoveryResult result,
            ISet<string> builtInNames,
            ISet<string> pluginNames,
            ISet<string> pluginMcpNames,
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
                        RoslynCompilerPayload = hostServices.RoslynCompilerPayload,
                        HostServices = hostServices
                    };

                    var tools = provider.GetTools(pluginContext) ?? Array.Empty<IUnityMcpTool>();
                    foreach (var tool in tools)
                    {
                        RegisterPluginTool(tool, attribute, assembly, registry, paths, logger, result, builtInNames, pluginNames, pluginMcpNames);
                    }
                }
                catch (Exception exception)
                {
                    logger?.Exception("plugin_provider_discover_failed", exception);
                }
            }
        }

        private static void ProcessManifestPlugin(
            UnityMcpInstalledPlugin plugin,
            AgentToolRegistry registry,
            AgentBridgePaths paths,
            FileAgentBridgeLogger logger,
            UnityMcpPluginDiscoveryResult result,
            ISet<string> builtInNames,
            ISet<string> pluginNames,
            ISet<string> pluginMcpNames,
            UnityMcpPluginHostServices hostServices)
        {
            var identity = InstallationIdentity(plugin, paths.ProjectRoot);
            var provider = ManifestProviders.GetOrCreate(identity, () => Activator.CreateInstance(plugin.ProviderType) as IUnityMcpToolProvider);
            ConfigurationProviders[plugin.PluginId] = provider;
            if (provider == null)
            {
                throw new InvalidOperationException($"Provider '{plugin.ProviderType?.FullName}' could not be instantiated.");
            }

            var attribute = plugin.ProviderType.GetCustomAttribute<UnityMcpPluginAttribute>();
            if (attribute == null)
            {
                throw new InvalidOperationException($"Provider '{plugin.ProviderType.FullName}' is missing UnityMcpPluginAttribute.");
            }

            var pluginContext = new UnityMcpPluginContext
            {
                ProjectRoot = paths.ProjectRoot,
                AssemblyName = plugin.Assembly.GetName().Name ?? string.Empty,
                PluginId = plugin.PluginId,
                PluginVersion = plugin.Version,
                PluginRoot = plugin.PackageRoot,
                Payloads = plugin.Payloads,
                RoslynCompilerPayload = hostServices.RoslynCompilerPayload,
                HostServices = hostServices
            };
            UnityMcpPluginContractValidator.ValidatePluginContext(pluginContext);

            var catalogCount = result.Catalog.tools.Count;
            var tools = provider.GetTools(pluginContext) ?? Array.Empty<IUnityMcpTool>();
            foreach (var tool in tools)
            {
                RegisterPluginTool(tool, attribute, plugin.Assembly, registry, paths, logger, result, builtInNames, pluginNames, pluginMcpNames);
            }

            plugin.Exposed = result.Catalog.tools.Count > catalogCount;
            if (!plugin.Exposed)
            {
                ManifestProviders.Reject(identity);
                plugin.DiagnosticCode = UnityMcpPluginDiagnosticCodes.ProviderInvalid;
                plugin.DiagnosticMessage = "Provider returned no valid tools.";
            }
            else
            {
                ManifestProviders.Activate(identity);
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
            ISet<string> pluginNames,
            ISet<string> pluginMcpNames)
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

                if (pluginNames.Contains(bridgeToolName))
                {
                    logger?.Warning("plugin_tool_conflict_plugin", $"Plugin tool '{bridgeToolName}' conflicts with another plugin tool and was rejected.");
                    return;
                }

                var protocolMetadata = tool as IUnityMcpToolProtocolMetadata;
                var mcpToolName = protocolMetadata != null
                    ? protocolMetadata.McpName
                    : McpToolNameMapper.ToCanonicalMcpName(bridgeToolName);
                if (pluginMcpNames.Contains(mcpToolName))
                {
                    logger?.Warning("plugin_mcp_name_conflict", $"Plugin tool '{bridgeToolName}' declares MCP tool '{mcpToolName}', which conflicts with an existing tool and was rejected.");
                    return;
                }

                var resolvedSchema = ResolveSchema(tool.InputSchema, assembly, paths.ProjectRoot);
                var catalogTool = new UnityMcpPluginCatalogTool
                {
                    pluginId = attribute.PluginId,
                    pluginVersion = attribute.PluginVersion,
                    assemblyName = assembly.GetName().Name ?? string.Empty,
                    bridgeTool = bridgeToolName,
                    mcpName = mcpToolName,
                    title = tool.Descriptor.Title,
                    description = tool.Descriptor.Description,
                    defaultTimeoutMs = tool.Descriptor.DefaultTimeoutMs,
                    allowedRuntimeModes = tool.Descriptor.AllowedRuntimeModes.ToString(),
                    sideEffect = tool.Descriptor.SideEffect.ToString(),
                    mayTriggerDomainReload = tool.Descriptor.MayTriggerDomainReload,
                    inputSchemaJson = resolvedSchema
                };
                UnityMcpPluginCatalogV1Validator.ValidateTool(catalogTool);
                if (result.Catalog.tools.Count >= UnityMcpPluginCatalog.MaxTools)
                {
                    logger?.Warning("plugin_catalog_tool_limit", $"Plugin tool '{bridgeToolName}' exceeds the {UnityMcpPluginCatalog.MaxTools}-tool catalog limit and was rejected.");
                    return;
                }

                result.Catalog.tools.Add(catalogTool);
                var catalogJson = JsonUtil.SerializeObject(result.Catalog);
                result.Catalog.tools.RemoveAt(result.Catalog.tools.Count - 1);
                if (Encoding.UTF8.GetByteCount(catalogJson) > UnityMcpPluginCatalog.MaxUtf8Bytes)
                {
                    logger?.Warning("plugin_catalog_size_limit", $"Plugin tool '{bridgeToolName}' exceeds the {UnityMcpPluginCatalog.MaxUtf8Bytes}-byte catalog limit and was rejected.");
                    return;
                }

                var adapted = new UnityMcpPluginToolAdapter(tool, paths.ProjectRoot, paths.TempRoot);
                registry.Register(adapted);
                result.Catalog.tools.Add(catalogTool);
                pluginNames.Add(bridgeToolName);
                pluginMcpNames.Add(mcpToolName);
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

        private static string WriteCatalog(string outputPath, UnityMcpPluginCatalog catalog, FileAgentBridgeLogger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonUtil.SerializeObject(catalog);
                if (catalog.version != UnityMcpPluginCatalog.CurrentVersion ||
                    catalog.tools == null ||
                    catalog.tools.Count > UnityMcpPluginCatalog.MaxTools ||
                    Encoding.UTF8.GetByteCount(json) > UnityMcpPluginCatalog.MaxUtf8Bytes)
                {
                    throw new InvalidOperationException("Plugin catalog violates the frozen Catalog V1 envelope limits.");
                }

                var bytes = new UTF8Encoding(false).GetBytes(json);
                var tempPath = outputPath + ".tmp";
                var backupPath = outputPath + ".bak";
                try
                {
                    File.WriteAllBytes(tempPath, bytes);
                    if (File.Exists(outputPath))
                    {
                        File.Replace(tempPath, outputPath, backupPath);
                    }
                    else
                    {
                        File.Move(tempPath, outputPath);
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception exception)
            {
                logger?.Exception("plugin_catalog_write_failed", exception);
                throw;
            }
        }
    }

    public sealed class UnityMcpPluginDiscoveryResult
    {
        public UnityMcpPluginCatalog Catalog { get; } = new UnityMcpPluginCatalog();

        public string CapabilityRevision { get; internal set; } = "missing";

        public List<UnityMcpInstalledPlugin> InstalledPlugins { get; } = new List<UnityMcpInstalledPlugin>();
    }

    public sealed class UnityMcpPluginHostServices
    {
        public AgentBridgeSettings Settings { get; set; }

        public AgentCommandQueue Queue { get; set; }

        public AgentToolRegistry Registry { get; set; }

        public FileAgentBridgeLogger Logger { get; set; }

        public UnityMcpRoslynCompilerPayload RoslynCompilerPayload { get; set; }
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
