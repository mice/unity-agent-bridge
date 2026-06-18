using System;

namespace UnityMcp.AgentBridge
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AgentToolAttribute : Attribute
    {
        public AgentToolAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
