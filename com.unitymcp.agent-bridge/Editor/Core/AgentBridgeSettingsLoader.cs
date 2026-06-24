using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    public static class AgentBridgeSettingsLoader
    {
        public static SettingsLoadResult Load()
        {
            var canonicalSettings = TryLoadCanonicalAsset();
            if (canonicalSettings != null)
            {
                return canonicalSettings;
            }

            var guids = AssetDatabase.FindAssets("t:AgentBridgeSettings");
            if (guids == null || guids.Length == 0)
            {
                var settings = CreateDefaultSettings();
                return SettingsLoadResult.StartWithWarning(settings, "AGENTBRIDGE_SETTINGS_MISSING", "AgentBridgeSettings asset was not found. Using defaults.");
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settingsAsset = AssetDatabase.LoadAssetAtPath<AgentBridgeSettings>(assetPath);
            if (settingsAsset == null)
            {
                return SettingsLoadResult.Stop("AGENTBRIDGE_SETTINGS_DESERIALIZE_FAILED", "AgentBridgeSettings asset exists but could not be deserialized.");
            }

            if (!TryValidate(settingsAsset, out var validationMessage))
            {
                return SettingsLoadResult.Stop("AGENTBRIDGE_SETTINGS_INVALID_FIELD", validationMessage);
            }

            return SettingsLoadResult.Start(settingsAsset, assetPath);
        }

        public static string DefaultAssetPath => "Assets/Settings/AgentBridgeSettings.asset";

        public static AgentBridgeSettings CreateDefaultAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultAssetPath) ?? "Assets");
            var settings = CreateDefaultSettings();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        internal static AgentBridgeSettings CreateDefaultSettings()
        {
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            settings.roslynExecutionEnabled = true;
            settings.allowedStaticMethods = CreateDefaultAllowedStaticMethods();
            settings.pluginRegistrations = CreateDefaultPluginRegistrations();
            return settings;
        }

        private static SettingsLoadResult TryLoadCanonicalAsset()
        {
            var canonicalGuid = AssetDatabase.AssetPathToGUID(DefaultAssetPath);
            if (string.IsNullOrWhiteSpace(canonicalGuid))
            {
                return null;
            }

            var settingsAsset = AssetDatabase.LoadAssetAtPath<AgentBridgeSettings>(DefaultAssetPath);
            if (settingsAsset == null)
            {
                return SettingsLoadResult.Stop("AGENTBRIDGE_SETTINGS_DESERIALIZE_FAILED", "AgentBridgeSettings asset exists but could not be deserialized.");
            }

            if (!TryValidate(settingsAsset, out var validationMessage))
            {
                return SettingsLoadResult.Stop("AGENTBRIDGE_SETTINGS_INVALID_FIELD", validationMessage);
            }

            return SettingsLoadResult.Start(settingsAsset, DefaultAssetPath);
        }

        private static bool TryValidate(AgentBridgeSettings settings, out string validationMessage)
        {
            if (settings.pollIntervalMs <= 0)
            {
                validationMessage = "pollIntervalMs must be greater than 0.";
                return false;
            }

            if (settings.maxConcurrent <= 0)
            {
                validationMessage = "maxConcurrent must be greater than 0.";
                return false;
            }

            if (settings.maxToolDurationMs <= 0)
            {
                validationMessage = "maxToolDurationMs must be greater than 0.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.pluginCatalogPath))
            {
                validationMessage = "pluginCatalogPath is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.tempRoot) || string.IsNullOrWhiteSpace(settings.logRoot) || string.IsNullOrWhiteSpace(settings.metricsPath))
            {
                validationMessage = "tempRoot, logRoot and metricsPath are required.";
                return false;
            }

            if (settings.allowedStaticMethods != null && settings.allowedStaticMethods.Any(entry => entry != null && string.IsNullOrWhiteSpace(entry.typeName)))
            {
                validationMessage = "allowedStaticMethods entries must not have an empty typeName.";
                return false;
            }

            if (settings.allowedStaticMethods != null && settings.allowedStaticMethods.Any(entry => entry != null && string.IsNullOrWhiteSpace(entry.methodName)))
            {
                validationMessage = "allowedStaticMethods entries must not have an empty methodName.";
                return false;
            }

            if (settings.allowedStaticMethods != null && settings.allowedStaticMethods.Any(entry => entry != null && entry.maxDurationMs > settings.maxToolDurationMs))
            {
                validationMessage = "allowedStaticMethods maxDurationMs must not exceed settings.maxToolDurationMs.";
                return false;
            }

            if (settings.pluginRegistrations != null)
            {
                for (var index = 0; index < settings.pluginRegistrations.Count; index++)
                {
                    var registration = settings.pluginRegistrations[index];
                    if (registration == null || !registration.enabled)
                    {
                        continue;
                    }

                    switch (registration.kind)
                    {
                        case UnityMcpPluginRegistrationKind.AsmdefAssembly:
                            if (string.IsNullOrWhiteSpace(registration.assemblyName))
                            {
                                validationMessage = $"pluginRegistrations[{index}] must declare assemblyName for asmdef plugins.";
                                return false;
                            }

                            break;
                        case UnityMcpPluginRegistrationKind.ManagedDll:
                            if (string.IsNullOrWhiteSpace(registration.dllPath))
                            {
                                validationMessage = $"pluginRegistrations[{index}] must declare dllPath for managed DLL plugins.";
                                return false;
                            }

                            if (Path.IsPathRooted(registration.dllPath))
                            {
                                validationMessage = $"pluginRegistrations[{index}] dllPath must be project-relative.";
                                return false;
                            }

                            break;
                        default:
                            validationMessage = $"pluginRegistrations[{index}] uses unsupported registration kind '{registration.kind}'.";
                            return false;
                    }
                }
            }

            validationMessage = null;
            return true;
        }

        private static System.Collections.Generic.List<AllowedStaticMethodEntry> CreateDefaultAllowedStaticMethods()
        {
            return new System.Collections.Generic.List<AllowedStaticMethodEntry>
            {
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_ok",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestOk),
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "read",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestOk().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_echo",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestEcho),
                    argsSchemaPath = "Documentation~/schemas/agentbridge.selftest_echo.args.schema.json",
                    parameterDtoTypeName = typeof(AgentBridgeStaticMethodEchoArgs).FullName,
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "read",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestEcho().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_throw",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestThrow),
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestThrow().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_missing_done",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestMissingDone),
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestMissingDone().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.roslyn_spike",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.RunRoslynSpike),
                    argsSchemaPath = "Documentation~/schemas/agentbridge.roslyn_spike.args.schema.json",
                    parameterDtoTypeName = typeof(RoslynSpikeArgs).FullName,
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.RunRoslynSpike().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.prepare_mcp_runtime",
                    typeName = "UnityMcp.AgentBridge.Mcp.McpRuntimeSmokeMethods",
                    methodName = "PrepareMcpRuntime",
                    argsSchemaPath = string.Empty,
                    parameterDtoTypeName = "UnityMcp.AgentBridge.Mcp.PrepareMcpRuntimeArgs",
                    requiresMainThread = true,
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "McpRuntimeSmokeMethods.PrepareMcpRuntime().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.prepare_mcp_runtime_and_apply_codex_config",
                    typeName = "UnityMcp.AgentBridge.Mcp.McpRuntimeSmokeMethods",
                    methodName = "PrepareRuntimeAndApplyCodexConfig",
                    argsSchemaPath = string.Empty,
                    parameterDtoTypeName = "UnityMcp.AgentBridge.Mcp.PrepareMcpRuntimeArgs",
                    requiresMainThread = true,
                    maxDurationMs = 120000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "McpRuntimeSmokeMethods.PrepareRuntimeAndApplyCodexConfig().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.focused_editmode_tests",
                    typeName = "UnityMcp.AgentBridge.Mcp.McpFocusedEditModeTestMethods",
                    methodName = "RunFocusedTests",
                    argsSchemaPath = "Documentation~/schemas/agentbridge.focused_editmode_tests.args.schema.json",
                    parameterDtoTypeName = "UnityMcp.AgentBridge.Mcp.RunFocusedEditModeTestsArgs",
                    requiresMainThread = true,
                    maxDurationMs = 120000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "McpFocusedEditModeTestMethods.RunFocusedTests().Done"
                }
            };
        }

        private static System.Collections.Generic.List<UnityMcpPluginRegistration> CreateDefaultPluginRegistrations()
        {
            return new System.Collections.Generic.List<UnityMcpPluginRegistration>
            {
                new UnityMcpPluginRegistration
                {
                    enabled = true,
                    kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                    assemblyName = "UnityMcp.BuiltInPlugins.ProjectInfo",
                    providerTypeName = "UnityMcp.BuiltInPlugins.ProjectInfo.ProjectInfoProvider"
                },
                new UnityMcpPluginRegistration
                {
                    enabled = true,
                    kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                    assemblyName = "UnityMcp.BuiltInPlugins.EditorBasics",
                    providerTypeName = "UnityMcp.BuiltInPlugins.EditorBasics.EditorBasicsProvider"
                },
                new UnityMcpPluginRegistration
                {
                    enabled = true,
                    kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                    assemblyName = "UnityMcp.BuiltInPlugins.UnityQueries",
                    providerTypeName = "UnityMcp.BuiltInPlugins.UnityQueries.UnityQueriesProvider"
                },
                new UnityMcpPluginRegistration
                {
                    enabled = true,
                    kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                    assemblyName = "UnityMcp.BuiltInPlugins.TestRunner",
                    providerTypeName = "UnityMcp.BuiltInPlugins.TestRunner.TestRunnerProvider"
                },
                new UnityMcpPluginRegistration
                {
                    enabled = true,
                    kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                    assemblyName = "UnityMcp.BuiltInPlugins.RoslynExecution",
                    providerTypeName = "UnityMcp.BuiltInPlugins.RoslynExecution.RoslynExecutionProvider"
                }
            };
        }
    }

    public sealed class SettingsLoadResult
    {
        private SettingsLoadResult()
        {
        }

        public AgentBridgeSettings Settings { get; private set; }

        public bool ShouldStart { get; private set; }

        public string WarningCode { get; private set; }

        public string WarningMessage { get; private set; }

        public string AssetPath { get; private set; }

        public static SettingsLoadResult Start(AgentBridgeSettings settings, string assetPath)
        {
            return new SettingsLoadResult
            {
                Settings = settings,
                ShouldStart = settings != null && settings.enabled,
                AssetPath = assetPath
            };
        }

        public static SettingsLoadResult StartWithWarning(AgentBridgeSettings settings, string warningCode, string warningMessage)
        {
            return new SettingsLoadResult
            {
                Settings = settings,
                ShouldStart = settings != null && settings.enabled,
                WarningCode = warningCode,
                WarningMessage = warningMessage
            };
        }

        public static SettingsLoadResult Stop(string warningCode, string warningMessage)
        {
            return new SettingsLoadResult
            {
                Settings = null,
                ShouldStart = false,
                WarningCode = warningCode,
                WarningMessage = warningMessage
            };
        }
    }
}
