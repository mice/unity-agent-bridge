using NUnit.Framework;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class CoreModelTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_021.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_021")]
        public void AgentBridgeSettings_DefaultValuesMatchFrozenSpec()
        {
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();

            Assert.That(settings.enabled, Is.True);
            Assert.That(settings.roslynExecutionEnabled, Is.False);
            Assert.That(settings.monoBehaviourFindReference2ProviderEnabled, Is.False);
            Assert.That(settings.pollIntervalMs, Is.EqualTo(200));
            Assert.That(settings.maxPollIntervalMs, Is.EqualTo(2000));
            Assert.That(settings.compileBackoffMs, Is.EqualTo(1000));
            Assert.That(settings.maxConcurrent, Is.EqualTo(1));
            Assert.That(settings.tempRoot, Is.EqualTo("Temp/AgentBridge"));
            Assert.That(settings.logRoot, Is.EqualTo("Library/AgentBridge/logs"));
            Assert.That(settings.metricsPath, Is.EqualTo("Library/AgentBridge/metrics.json"));
            Assert.That(settings.logLevel, Is.EqualTo("info"));
            Assert.That(settings.mainThreadWarnAfterMs, Is.EqualTo(5000));
            Assert.That(settings.maxToolDurationMs, Is.EqualTo(300000));
            Assert.That(settings.metricsRetentionDays, Is.EqualTo(7));
            Assert.That(settings.logRetentionDays, Is.EqualTo(7));
            Assert.That(settings.pluginCatalogPath, Is.EqualTo("Library/AgentBridge/plugin-catalog.json"));
            Assert.That(settings.allowedStaticMethods, Is.Not.Null);
            Assert.That(settings.pluginRegistrations, Is.Not.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_022.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_022")]
        public void AllowedStaticMethodEntry_DefaultsMatchReadOnlyContract()
        {
            var entry = new AllowedStaticMethodEntry();

            Assert.That(entry.requiresMainThread, Is.True);
            Assert.That(entry.maxDurationMs, Is.EqualTo(60000));
            Assert.That(entry.sideEffects, Is.EqualTo("read"));
            Assert.That(entry.allowAssetDatabaseRefresh, Is.False);
            Assert.That(entry.allowAssetDatabaseSaveAssets, Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_023.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_023")]
        public void DescriptorAttributeAndContext_RoundTripAssignedValues()
        {
            var descriptor = new ToolDescriptor
            {
                Name = "unity.ping",
                SchemaVersion = "1.0",
                Description = "Ping Unity.",
                AllowedModes = ToolExecutionModes.EditAndPlay,
                SideEffect = ToolSideEffect.RunsUserCode,
                MayTriggerDomainReload = true,
                ArgsSchemaPath = "Documentation~/schemas/ping.schema.json"
            };
            var command = new AgentCommand
            {
                schemaVersion = "1.0",
                commandId = "cmd-023",
                tool = "unity.ping",
                timeoutMs = 2500,
                createdAt = "2026-06-05T10:00:00Z",
                rawArgsJson = "{}"
            };
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            var context = new AgentToolContext
            {
                Command = command,
                RawArgsJson = command.rawArgsJson,
                Settings = settings
            };
            var attribute = new AgentToolAttribute("unity.ping");

            Assert.That(descriptor.Name, Is.EqualTo("unity.ping"));
            Assert.That(descriptor.SchemaVersion, Is.EqualTo("1.0"));
            Assert.That(descriptor.Description, Is.EqualTo("Ping Unity."));
            Assert.That(descriptor.AllowedModes, Is.EqualTo(ToolExecutionModes.EditAndPlay));
            Assert.That(descriptor.SideEffect, Is.EqualTo(ToolSideEffect.RunsUserCode));
            Assert.That(descriptor.MayTriggerDomainReload, Is.True);
            Assert.That(descriptor.ArgsSchemaPath, Is.EqualTo("Documentation~/schemas/ping.schema.json"));
            Assert.That(context.Command, Is.SameAs(command));
            Assert.That(context.RawArgsJson, Is.EqualTo("{}"));
            Assert.That(context.Settings, Is.SameAs(settings));
            Assert.That(attribute.Name, Is.EqualTo("unity.ping"));
        }

        [Test]
        [Category("AGB_Core")]
        public void ToolDescriptorDisplay_FormatsGovernedLabels()
        {
            Assert.That(ToolDescriptorDisplay.GetAllowedModeSummary(ToolExecutionModes.Edit), Is.EqualTo("Edit Mode"));
            Assert.That(ToolDescriptorDisplay.GetAllowedModeSummary(ToolExecutionModes.EditAndPlay), Is.EqualTo("Edit Mode, Play Mode"));
            Assert.That(ToolDescriptorDisplay.GetSideEffectLabel(ToolSideEffect.None), Is.EqualTo("No project changes"));
            Assert.That(ToolDescriptorDisplay.GetSideEffectLabel(ToolSideEffect.RunsUserCode), Is.EqualTo("Runs user code"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_024.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_024")]
        public void ToolResultFactories_PopulateFailureShape()
        {
            var invalid = ToolResult.InvalidArgs("BAD_ARGS", "bad args");
            var unsupported = ToolResult.Unsupported("UNSUPPORTED", "unsupported");

            Assert.That(invalid.success, Is.False);
            Assert.That(invalid.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(invalid.summary, Is.EqualTo("bad args"));
            Assert.That(invalid.errors, Has.Count.EqualTo(1));
            Assert.That(invalid.errors[0].code, Is.EqualTo("BAD_ARGS"));
            Assert.That(invalid.errors[0].message, Is.EqualTo("bad args"));
            Assert.That(unsupported.success, Is.False);
            Assert.That(unsupported.status, Is.EqualTo(ToolResultStatus.Unsupported));
            Assert.That(unsupported.summary, Is.EqualTo("unsupported"));
            Assert.That(unsupported.errors, Has.Count.EqualTo(1));
            Assert.That(unsupported.errors[0].code, Is.EqualTo("UNSUPPORTED"));
            Assert.That(unsupported.errors[0].message, Is.EqualTo("unsupported"));
        }

        [Test]
        [Category("AGB_Core")]
        public void PluginContractValidator_RejectsInvalidDescriptorShape()
        {
            var descriptor = new UnityMcpToolDescriptor
            {
                Name = "unity.sample.status",
                Title = string.Empty,
                Description = "desc",
                DefaultTimeoutMs = 0,
                AllowedRuntimeModes = UnityMcpToolRuntimeModes.None
            };

            Assert.That(() => UnityMcpPluginContractValidator.ValidateDescriptor(descriptor), Throws.ArgumentException);
        }

        [Test]
        [Category("AGB_Core")]
        public void PluginContractValidator_RejectsMissingSchemaValue()
        {
            var schema = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.AssetPath,
                Value = string.Empty
            };

            Assert.That(() => UnityMcpPluginContractValidator.ValidateSchema(schema), Throws.ArgumentException);
        }

        [Test]
        [Category("AGB_Core")]
        public void PluginRegistration_DefaultsMatchDiscoveryContract()
        {
            var registration = new UnityMcpPluginRegistration();

            Assert.That(registration.enabled, Is.True);
            Assert.That(registration.kind, Is.EqualTo(UnityMcpPluginRegistrationKind.AsmdefAssembly));
            Assert.That(registration.assemblyName, Is.Null);
            Assert.That(registration.dllPath, Is.Null);
            Assert.That(registration.providerTypeName, Is.Null);
        }

        [Test]
        [Category("AGB_Core")]
        public void AgentBridgePaths_ResolvesPluginCatalogPath()
        {
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            var paths = new AgentBridgePaths("D:/ProjCommon/UnityMCPTools/UnityMCP", settings);

            StringAssert.EndsWith("Library\\AgentBridge\\plugin-catalog.json", paths.PluginCatalogPath);
        }
    }
}
