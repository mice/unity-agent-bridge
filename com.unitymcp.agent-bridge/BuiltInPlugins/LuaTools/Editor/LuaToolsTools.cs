using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.LuaTools
{
    internal sealed class UnityLuaLintTool : IUnityMcpTool
    {
        private readonly LuaToolsAvailability _availability;

        public UnityLuaLintTool(LuaToolsAvailability availability)
        {
            _availability = availability;
        }

        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.lua.lint",
            Title = "Unity Lua Lint",
            Description = "Run Lua static analysis over a project-relative file or directory.",
            DefaultTimeoutMs = LuaToolsContracts.DefaultTimeoutMs,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = LuaToolsSchemas.Lint
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!LuaToolsJson.TryDeserializeArgs<LuaLintArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            return new LuaToolsService(_availability).RunLint(context, args ?? new LuaLintArgs(), cancellation);
        }
    }

    internal sealed class UnityLuaCompileTool : IUnityMcpTool
    {
        private readonly LuaToolsAvailability _availability;

        public UnityLuaCompileTool(LuaToolsAvailability availability)
        {
            _availability = availability;
        }

        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.lua.compile",
            Title = "Unity Lua Compile",
            Description = "Validate Lua syntax over a project-relative file, directory, or configured Lua source roots.",
            DefaultTimeoutMs = LuaToolsContracts.DefaultTimeoutMs,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = LuaToolsSchemas.Compile
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!LuaToolsJson.TryDeserializeArgs<LuaCompileArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            return new LuaToolsService(_availability).RunCompile(context, args ?? new LuaCompileArgs(), cancellation);
        }
    }
}
