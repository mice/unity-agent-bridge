using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentToolRegistry
    {
        private readonly Dictionary<string, IAgentTool> _tools = new Dictionary<string, IAgentTool>(StringComparer.Ordinal);
        private readonly FileAgentBridgeLogger _logger;

        public AgentToolRegistry(FileAgentBridgeLogger logger = null)
        {
            _logger = logger;
        }

        public void Discover()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException reflectionException)
                {
                    types = reflectionException.Types.Where(type => type != null).ToArray();
                }
                catch (Exception exception)
                {
                    _logger?.Exception("registry_discover_assembly_failed", exception);
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(IAgentTool).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (type.GetCustomAttribute<AgentToolAttribute>() == null)
                    {
                        continue;
                    }

                    try
                    {
                        var tool = Activator.CreateInstance(type) as IAgentTool;
                        if (tool != null)
                        {
                            Register(tool);
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger?.Exception("registry_discover_tool_failed", exception);
                    }
                }
            }
        }

        public void Register(IAgentTool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            ValidateDescriptor(tool.Descriptor, nameof(tool));

            if (_tools.ContainsKey(tool.Descriptor.Name))
            {
                return;
            }

            _tools.Add(tool.Descriptor.Name, tool);
        }

        public bool TryGetTool(string toolName, out IAgentTool tool)
        {
            return _tools.TryGetValue(toolName, out tool);
        }

        public IReadOnlyList<ToolDescriptor> ListTools()
        {
            return _tools.Values.Select(tool => tool.Descriptor).OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal).ToArray();
        }

        private static void ValidateDescriptor(ToolDescriptor descriptor, string paramName)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Name))
            {
                throw new ArgumentException("Tool descriptor and name are required.", paramName);
            }

            if (string.IsNullOrWhiteSpace(descriptor.Description))
            {
                throw new ArgumentException($"Tool '{descriptor.Name}' must declare a description.", paramName);
            }

            if (descriptor.AllowedModes == ToolExecutionModes.None)
            {
                throw new ArgumentException($"Tool '{descriptor.Name}' must declare at least one allowed runtime mode.", paramName);
            }
        }
    }
}
