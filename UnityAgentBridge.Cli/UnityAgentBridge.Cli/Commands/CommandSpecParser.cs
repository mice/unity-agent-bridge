using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.Cli.Commands;

internal static class CommandSpecParser
{
    public static bool HasHelpFlag(string[] argv)
    {
        foreach (var arg in argv)
        {
            if (string.Equals(arg, "-h", StringComparison.Ordinal) || string.Equals(arg, "--help", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new CommandValidationException("Missing command.");
        }

        var commandName = args[0];
        var reader = new TokenReader(args, 1);
        var global = new GlobalOptions();
        var spec = commandName switch
        {
            "ping" => ParsePing(reader, global),
            "project_info" => ParseProjectInfo(reader, global),
            "compile" => ParseCompile(reader, global),
            "console" => ParseConsole(reader, global),
            "assetdatabase_search" => ParseAssetDatabaseSearch(reader, global),
            "get_editor_state" => ParseGetEditorState(reader, global),
            "open_scene" => ParseOpenScene(reader, global),
            "get_hierarchy" => ParseGetHierarchy(reader, global),
            "read_report" => ParseReadReport(reader, global),
            "get_selection_info" => ParseGetSelectionInfo(reader, global),
            "get_gameobject_component_info" => ParseGetGameObjectComponentInfo(reader, global),
            "run-static" => ParseRunStatic(reader, global),
            "diagnostic" => ParseDiagnostic(reader, global),
            "test-edit" => ParseTestEdit(reader, global),
            "test-play" => ParseTestPlay(reader, global),
            "self-test" => ParseSelfTest(reader, global),
            "bridge-health" => ParseBridgeHealth(reader, global),
            "bridge-submit-only" => ParseBridgeSubmitOnly(reader, global),
            "bridge-wait-result" => ParseBridgeWaitResult(reader, global),
            "mcp-echo" => ParseMcpEcho(reader, global),
            _ => throw new CommandValidationException($"Unknown command '{commandName}'.")
        };

        reader.EnsureFullyConsumed();
        return new ParsedCommand(spec, global);
    }

    private static CommandSpec ParsePing(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 5000;
        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("unity.ping", timeoutMs, "{}");
    }

    private static CommandSpec ParseProjectInfo(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("unity.project.get_info", timeoutMs, "{}");
    }

    private static CommandSpec ParseCompile(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 60000;
        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("unity.compile", timeoutMs, "{}");
    }

    private static CommandSpec ParseConsole(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        var types = new List<string>();
        var count = 100;
        string? filter = null;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--type", out var parsedType))
            {
                types.Add(parsedType);
                continue;
            }

            if (reader.TryConsumeIntOption("--count", out var parsedCount))
            {
                count = parsedCount;
                continue;
            }

            if (reader.TryConsumeStringOption("--filter", out var parsedFilter))
            {
                filter = parsedFilter;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (types.Count == 0)
        {
            types.Add("error");
        }

        if (types.Count > 3)
        {
            throw new CommandValidationException("--type may be specified at most three times.");
        }

        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!IsValidConsoleType(type))
            {
                throw new CommandValidationException("--type must be one of: error, warning, info.");
            }

            if (!seenTypes.Add(type))
            {
                throw new CommandValidationException("--type values must be unique.");
            }
        }

        if (count < 0 || count > 1000)
        {
            throw new CommandValidationException("--count must be in the range 0..1000.");
        }

        var argsObject = new JObject
        {
            ["types"] = new JArray(types),
            ["count"] = count
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            argsObject["filter"] = filter;
        }

        return new CommandSpec("unity.get_console", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseAssetDatabaseSearch(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        string? query = null;
        var folders = new List<string>();
        int? offset = null;
        int? limit = null;
        var includeDetails = false;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--query", out var parsedQuery))
            {
                query = parsedQuery;
                continue;
            }

            if (reader.TryConsumeStringOption("--folder", out var parsedFolder))
            {
                folders.Add(parsedFolder);
                continue;
            }

            if (reader.TryConsumeIntOption("--offset", out var parsedOffset))
            {
                offset = parsedOffset;
                continue;
            }

            if (reader.TryConsumeIntOption("--limit", out var parsedLimit))
            {
                limit = parsedLimit;
                continue;
            }

            if (reader.TryConsumeFlag("--include-details"))
            {
                includeDetails = true;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new CommandValidationException("--query is required.");
        }

        if (offset.HasValue && offset.Value < 0)
        {
            throw new CommandValidationException("--offset must be greater than or equal to 0.");
        }

        if (limit.HasValue && (limit.Value <= 0 || limit.Value > 200))
        {
            throw new CommandValidationException("--limit must be in the range 1..200.");
        }

        var argsObject = new JObject
        {
            ["query"] = query
        };

        AddStringArray(argsObject, "folders", folders);

        if (offset.HasValue)
        {
            argsObject["offset"] = offset.Value;
        }

        if (limit.HasValue)
        {
            argsObject["limit"] = limit.Value;
        }

        if (includeDetails)
        {
            argsObject["includeDetails"] = true;
        }

        return new CommandSpec("unity.assetdatabase_search", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseGetSelectionInfo(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        var includeDetails = false;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeFlag("--include-details"))
            {
                includeDetails = true;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        var argsObject = new JObject();
        if (includeDetails)
        {
            argsObject["includeDetails"] = true;
        }

        return new CommandSpec("unity.get_selection_info", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseGetEditorState(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("unity.get_editor_state", timeoutMs, "{}");
    }

    private static CommandSpec ParseOpenScene(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        string? scenePath = null;
        string? mode = null;
        bool? setActive = null;
        bool? saveModifiedScenes = null;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--scene-path", out var parsedScenePath))
            {
                scenePath = parsedScenePath;
                continue;
            }

            if (reader.TryConsumeStringOption("--mode", out var parsedMode))
            {
                mode = parsedMode;
                continue;
            }

            if (reader.TryConsumeStringOption("--set-active", out var parsedSetActive))
            {
                setActive = ParseBooleanOption(parsedSetActive, "--set-active");
                continue;
            }

            if (reader.TryConsumeStringOption("--save-modified-scenes", out var parsedSaveModifiedScenes))
            {
                saveModifiedScenes = ParseBooleanOption(parsedSaveModifiedScenes, "--save-modified-scenes");
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            throw new CommandValidationException("--scene-path is required.");
        }

        if (!string.IsNullOrWhiteSpace(mode) &&
            !string.Equals(mode, "single", StringComparison.Ordinal) &&
            !string.Equals(mode, "additive", StringComparison.Ordinal))
        {
            throw new CommandValidationException("--mode must be one of: single, additive.");
        }

        var argsObject = new JObject
        {
            ["scenePath"] = scenePath
        };

        if (!string.IsNullOrWhiteSpace(mode))
        {
            argsObject["mode"] = mode;
        }

        if (setActive.HasValue)
        {
            argsObject["setActive"] = setActive.Value;
        }

        if (saveModifiedScenes.HasValue)
        {
            argsObject["saveModifiedScenes"] = saveModifiedScenes.Value;
        }

        return new CommandSpec("unity.open_scene", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseGetHierarchy(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        string? locator = null;
        int? maxDepth = null;
        int? limit = null;
        var includeComponents = false;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--locator", out var parsedLocator))
            {
                locator = parsedLocator;
                continue;
            }

            if (reader.TryConsumeIntOption("--max-depth", out var parsedMaxDepth))
            {
                maxDepth = parsedMaxDepth;
                continue;
            }

            if (reader.TryConsumeIntOption("--limit", out var parsedLimit))
            {
                limit = parsedLimit;
                continue;
            }

            if (reader.TryConsumeFlag("--include-components"))
            {
                includeComponents = true;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (maxDepth.HasValue && maxDepth.Value < 0)
        {
            throw new CommandValidationException("--max-depth must be greater than or equal to 0.");
        }

        if (limit.HasValue && (limit.Value <= 0 || limit.Value > 5000))
        {
            throw new CommandValidationException("--limit must be in the range 1..5000.");
        }

        var argsObject = new JObject();
        if (locator is not null)
        {
            argsObject["locator"] = locator;
        }

        if (maxDepth.HasValue)
        {
            argsObject["maxDepth"] = maxDepth.Value;
        }

        if (limit.HasValue)
        {
            argsObject["limit"] = limit.Value;
        }

        if (includeComponents)
        {
            argsObject["includeComponents"] = true;
        }

        return new CommandSpec("unity.get_hierarchy", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseReadReport(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        string? reportPath = null;
        string? jsonPointer = null;
        int? offset = null;
        int? limit = null;
        int? maxBytes = null;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--report-path", out var parsedReportPath))
            {
                reportPath = parsedReportPath;
                continue;
            }

            if (reader.TryConsumeStringOption("--json-pointer", out var parsedJsonPointer))
            {
                jsonPointer = parsedJsonPointer;
                continue;
            }

            if (reader.TryConsumeIntOption("--offset", out var parsedOffset))
            {
                offset = parsedOffset;
                continue;
            }

            if (reader.TryConsumeIntOption("--limit", out var parsedLimit))
            {
                limit = parsedLimit;
                continue;
            }

            if (reader.TryConsumeIntOption("--max-bytes", out var parsedMaxBytes))
            {
                maxBytes = parsedMaxBytes;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new CommandValidationException("--report-path is required.");
        }

        if (offset.HasValue && offset.Value < 0)
        {
            throw new CommandValidationException("--offset must be greater than or equal to 0.");
        }

        if (limit.HasValue && (limit.Value <= 0 || limit.Value > 500))
        {
            throw new CommandValidationException("--limit must be in the range 1..500.");
        }

        if (maxBytes.HasValue && (maxBytes.Value <= 0 || maxBytes.Value > 262144))
        {
            throw new CommandValidationException("--max-bytes must be in the range 1..262144.");
        }

        var argsObject = new JObject
        {
            ["reportPath"] = reportPath
        };

        if (jsonPointer is not null)
        {
            argsObject["jsonPointer"] = jsonPointer;
        }

        if (offset.HasValue)
        {
            argsObject["offset"] = offset.Value;
        }

        if (limit.HasValue)
        {
            argsObject["limit"] = limit.Value;
        }

        if (maxBytes.HasValue)
        {
            argsObject["maxBytes"] = maxBytes.Value;
        }

        return new CommandSpec("unity.read_report", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseGetGameObjectComponentInfo(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 10000;
        string? locator = null;
        string? componentName = null;
        int? componentIndex = null;
        string? propertyMode = null;
        int? propertyLimit = null;
        int? arrayElementLimit = null;
        int? stringMaxLength = null;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--locator", out var parsedLocator))
            {
                locator = parsedLocator;
                continue;
            }

            if (reader.TryConsumeStringOption("--component-name", out var parsedComponentName))
            {
                componentName = parsedComponentName;
                continue;
            }

            if (reader.TryConsumeIntOption("--component-index", out var parsedComponentIndex))
            {
                componentIndex = parsedComponentIndex;
                continue;
            }

            if (reader.TryConsumeStringOption("--property-mode", out var parsedPropertyMode))
            {
                propertyMode = parsedPropertyMode;
                continue;
            }

            if (reader.TryConsumeIntOption("--property-limit", out var parsedPropertyLimit))
            {
                propertyLimit = parsedPropertyLimit;
                continue;
            }

            if (reader.TryConsumeIntOption("--array-element-limit", out var parsedArrayElementLimit))
            {
                arrayElementLimit = parsedArrayElementLimit;
                continue;
            }

            if (reader.TryConsumeIntOption("--string-max-length", out var parsedStringMaxLength))
            {
                stringMaxLength = parsedStringMaxLength;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (componentIndex.HasValue && componentIndex.Value < 0)
        {
            throw new CommandValidationException("--component-index must be greater than or equal to 0.");
        }

        if (!string.IsNullOrWhiteSpace(propertyMode) &&
            !string.Equals(propertyMode, "debug", StringComparison.Ordinal) &&
            !string.Equals(propertyMode, "serialized", StringComparison.Ordinal))
        {
            throw new CommandValidationException("--property-mode must be one of: debug, serialized.");
        }

        if (propertyLimit.HasValue && (propertyLimit.Value < 0 || propertyLimit.Value > 1000))
        {
            throw new CommandValidationException("--property-limit must be in the range 0..1000.");
        }

        if (arrayElementLimit.HasValue && (arrayElementLimit.Value < 0 || arrayElementLimit.Value > 200))
        {
            throw new CommandValidationException("--array-element-limit must be in the range 0..200.");
        }

        if (stringMaxLength.HasValue && (stringMaxLength.Value < 16 || stringMaxLength.Value > 4000))
        {
            throw new CommandValidationException("--string-max-length must be in the range 16..4000.");
        }

        var argsObject = new JObject();
        if (!string.IsNullOrWhiteSpace(locator))
        {
            argsObject["locator"] = locator;
        }

        if (!string.IsNullOrWhiteSpace(componentName))
        {
            argsObject["componentName"] = componentName;
        }

        if (componentIndex.HasValue)
        {
            argsObject["componentIndex"] = componentIndex.Value;
        }

        if (!string.IsNullOrWhiteSpace(propertyMode))
        {
            argsObject["propertyMode"] = propertyMode;
        }

        if (propertyLimit.HasValue)
        {
            argsObject["propertyLimit"] = propertyLimit.Value;
        }

        if (arrayElementLimit.HasValue)
        {
            argsObject["arrayElementLimit"] = arrayElementLimit.Value;
        }

        if (stringMaxLength.HasValue)
        {
            argsObject["stringMaxLength"] = stringMaxLength.Value;
        }

        return new CommandSpec("unity.get_gameobject_component_info", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseRunStatic(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 60000;
        var parametersRaw = "{}";
        var positionals = new List<string>();

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeStringOption("--parameters", out var parsedParameters))
            {
                parametersRaw = parsedParameters;
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.PeekStartsWithDash())
            {
                throw new CommandValidationException($"Unknown option '{reader.Peek()}'.");
            }

            positionals.Add(reader.ConsumeRequiredValue("target"));
        }

        EnsurePositiveTimeout(timeoutMs);
        if (positionals.Count == 0 || positionals.Count > 2)
        {
            throw new CommandValidationException("run-static requires '<Type.Method>' or '<TypeName> <MethodName>'.");
        }

        string typeName;
        string methodName;
        if (positionals.Count == 2)
        {
            typeName = positionals[0];
            methodName = positionals[1];
        }
        else
        {
            var target = positionals[0];
            var separatorIndex = target.LastIndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= target.Length - 1)
            {
                throw new CommandValidationException("run-static target must be 'TypeName.MethodName' when methodName is omitted.");
            }

            typeName = target[..separatorIndex];
            methodName = target[(separatorIndex + 1)..];
        }

        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
        {
            throw new CommandValidationException("run-static requires both typeName and methodName.");
        }

        var argsObject = new JObject
        {
            ["typeName"] = typeName,
            ["methodName"] = methodName,
            ["parameters"] = ParseJsonObject(parametersRaw, "--parameters must be a JSON object.")
        };
        return new CommandSpec("unity.run_static_method", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseDiagnostic(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 120000;
        var positionals = new List<string>();

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.PeekStartsWithDash())
            {
                throw new CommandValidationException($"Unknown option '{reader.Peek()}'.");
            }

            positionals.Add(reader.ConsumeRequiredValue("diagnostic argument"));
        }

        EnsurePositiveTimeout(timeoutMs);
        if (positionals.Count != 2)
        {
            throw new CommandValidationException("diagnostic requires '<diagnosticType> <targetPath>'.");
        }

        var argsObject = new JObject
        {
            ["diagnosticType"] = positionals[0],
            ["targetPath"] = positionals[1]
        };
        return new CommandSpec("unity.run_diagnostic", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseTestEdit(TokenReader reader, GlobalOptions global)
    {
        return ParseTest(reader, global, "unity.run_editmode_tests", 120000);
    }

    private static CommandSpec ParseTestPlay(TokenReader reader, GlobalOptions global)
    {
        return ParseTest(reader, global, "unity.run_playmode_tests", 180000);
    }

    private static CommandSpec ParseSelfTest(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 120000;
        var includeEditModeCase = true;
        var includeDiagnosticCase = true;
        var continueOnFailure = true;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.TryConsumeFlag("--no-editmode"))
            {
                includeEditModeCase = false;
                continue;
            }

            if (reader.TryConsumeFlag("--no-diagnostic"))
            {
                includeDiagnosticCase = false;
                continue;
            }

            if (reader.TryConsumeFlag("--stop-on-failure"))
            {
                continueOnFailure = false;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        var argsObject = new JObject
        {
            ["includeEditModeCase"] = includeEditModeCase,
            ["includeDiagnosticCase"] = includeDiagnosticCase,
            ["continueOnFailure"] = continueOnFailure,
            ["timeoutMs"] = timeoutMs
        };
        return new CommandSpec("unity.agent_bridge_self_test", timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static CommandSpec ParseBridgeHealth(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 5000;
        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("unity.bridge_health", timeoutMs, "{}");
    }

    private static CommandSpec ParseBridgeSubmitOnly(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 15000;
        string? tool = null;
        var argsObject = new JObject();

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.TryConsumeStringOption("--tool", out var parsedTool))
            {
                tool = parsedTool;
                continue;
            }

            if (reader.TryConsumeStringOption("--args", out var parsedArgs))
            {
                argsObject = ParseJsonObject(parsedArgs, "--args must be a JSON object.");
                continue;
            }

            if (tool is null && !reader.PeekStartsWithDash())
            {
                tool = reader.ConsumeRequiredValue("tool");
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (string.IsNullOrWhiteSpace(tool))
        {
            throw new CommandValidationException("bridge-submit-only requires a tool name.");
        }

        var payload = new JObject
        {
            ["tool"] = tool,
            ["args"] = argsObject,
            ["timeoutMs"] = timeoutMs
        };
        return new CommandSpec("unity.bridge_submit_only", timeoutMs, payload.ToString(Formatting.None));
    }

    private static CommandSpec ParseBridgeWaitResult(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 15000;
        string? commandId = null;

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.TryConsumeStringOption("--command-id", out var parsedCommandId))
            {
                commandId = parsedCommandId;
                continue;
            }

            if (commandId is null && !reader.PeekStartsWithDash())
            {
                commandId = reader.ConsumeRequiredValue("commandId");
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new CommandValidationException("bridge-wait-result requires a commandId.");
        }

        var payload = new JObject
        {
            ["commandId"] = commandId,
            ["timeoutMs"] = timeoutMs
        };
        return new CommandSpec("unity.bridge_wait_result", timeoutMs, payload.ToString(Formatting.None));
    }

    private static CommandSpec ParseMcpEcho(TokenReader reader, GlobalOptions global)
    {
        var timeoutMs = 5000;
        var payload = new JObject();

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.TryConsumeStringOption("--payload", out var parsedPayload))
            {
                payload = ParseJsonObject(parsedPayload, "--payload must be a JSON object.");
                continue;
            }

            throw new CommandValidationException($"Unexpected argument '{reader.Peek()}'.");
        }

        EnsurePositiveTimeout(timeoutMs);
        return new CommandSpec("mcp.echo", timeoutMs, payload.ToString(Formatting.None));
    }

    private static CommandSpec ParseTest(TokenReader reader, GlobalOptions global, string tool, int defaultTimeoutMs)
    {
        var timeoutMs = defaultTimeoutMs;
        string? filter = null;
        var testNames = new List<string>();
        var assemblyNames = new List<string>();
        var categoryNames = new List<string>();
        var groupNames = new List<string>();

        while (reader.HasMore)
        {
            if (reader.TryConsumeGlobalOption(global))
            {
                continue;
            }

            if (reader.TryConsumeIntOption("--timeout-ms", out var parsedTimeout))
            {
                timeoutMs = parsedTimeout;
                continue;
            }

            if (reader.TryConsumeStringOption("--test-name", out var testName))
            {
                testNames.Add(testName);
                continue;
            }

            if (reader.TryConsumeStringOption("--assembly", out var assemblyName))
            {
                assemblyNames.Add(assemblyName);
                continue;
            }

            if (reader.TryConsumeStringOption("--category", out var categoryName))
            {
                categoryNames.Add(categoryName);
                continue;
            }

            if (reader.TryConsumeStringOption("--group", out var groupName))
            {
                groupNames.Add(groupName);
                continue;
            }

            if (reader.PeekStartsWithDash())
            {
                throw new CommandValidationException($"Unknown option '{reader.Peek()}'.");
            }

            if (filter is not null)
            {
                throw new CommandValidationException("Only one optional filter argument is allowed.");
            }

            filter = reader.ConsumeRequiredValue("filter");
        }

        EnsurePositiveTimeout(timeoutMs);
        EnsureNoWildcardValues(testNames, "--test-name");
        EnsureNoWildcardValues(assemblyNames, "--assembly");
        EnsureNoWildcardValues(categoryNames, "--category");
        EnsureNoWildcardValues(groupNames, "--group");

        if (!string.IsNullOrWhiteSpace(filter) && testNames.Count > 0)
        {
            throw new CommandValidationException("legacy [filter] cannot be combined with --test-name.");
        }

        var argsObject = new JObject();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            argsObject["filter"] = filter;
        }

        AddStringArray(argsObject, "testNames", testNames);
        AddStringArray(argsObject, "assemblyNames", assemblyNames);
        AddStringArray(argsObject, "categoryNames", categoryNames);
        AddStringArray(argsObject, "groupNames", groupNames);

        return new CommandSpec(tool, timeoutMs, argsObject.ToString(Formatting.None));
    }

    private static void EnsurePositiveTimeout(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            throw new CommandValidationException("--timeout-ms must be greater than 0.");
        }
    }

    private static bool IsValidConsoleType(string value)
    {
        return string.Equals(value, "error", StringComparison.Ordinal) ||
               string.Equals(value, "warning", StringComparison.Ordinal) ||
               string.Equals(value, "info", StringComparison.Ordinal);
    }

    private static void AddStringArray(JObject argsObject, string propertyName, List<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        var array = new JArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        argsObject[propertyName] = array;
    }

    private static void EnsureNoWildcardValues(List<string> values, string optionName)
    {
        foreach (var value in values)
        {
            if (value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0)
            {
                throw new CommandValidationException($"{optionName} does not support wildcard characters '*' or '?'.");
            }
        }
    }

    private static bool ParseBooleanOption(string rawValue, string optionName)
    {
        if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new CommandValidationException($"{optionName} must be either true or false.");
    }

    private static JObject ParseJsonObject(string rawJson, string errorMessage)
    {
        try
        {
            var node = JToken.Parse(rawJson);
            if (node is JObject jsonObject)
            {
                return jsonObject;
            }
        }
        catch (JsonReaderException)
        {
        }

        throw new CommandValidationException(errorMessage);
    }
}
