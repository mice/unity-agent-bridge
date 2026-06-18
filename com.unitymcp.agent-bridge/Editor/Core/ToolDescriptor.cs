using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    [Flags]
    public enum ToolExecutionModes
    {
        None = 0,
        Edit = 1 << 0,
        Play = 1 << 1,
        EditAndPlay = Edit | Play
    }

    public enum ToolSideEffect
    {
        None = 0,
        ReadsProject = 1,
        MutatesProject = 2,
        RunsUserCode = 3
    }

    public sealed class ToolDescriptor
    {
        public string Name { get; set; }

        public string SchemaVersion { get; set; }

        public string Description { get; set; }

        public ToolExecutionModes AllowedModes { get; set; }

        public ToolSideEffect SideEffect { get; set; }

        public bool MayTriggerDomainReload { get; set; }

        public string ArgsSchemaPath { get; set; }
    }

    public static class ToolDescriptorDisplay
    {
        public static string[] GetAllowedModeLabels(ToolExecutionModes allowedModes)
        {
            var labels = new List<string>(2);
            if ((allowedModes & ToolExecutionModes.Edit) != 0)
            {
                labels.Add("Edit Mode");
            }

            if ((allowedModes & ToolExecutionModes.Play) != 0)
            {
                labels.Add("Play Mode");
            }

            return labels.ToArray();
        }

        public static string GetAllowedModeSummary(ToolExecutionModes allowedModes)
        {
            var labels = GetAllowedModeLabels(allowedModes);
            if (labels.Length == 0)
            {
                return "None";
            }

            return string.Join(", ", labels);
        }

        public static string GetSideEffectLabel(ToolSideEffect sideEffect)
        {
            switch (sideEffect)
            {
                case ToolSideEffect.None:
                    return "No project changes";
                case ToolSideEffect.ReadsProject:
                    return "Reads project data";
                case ToolSideEffect.MutatesProject:
                    return "Mutates project state";
                case ToolSideEffect.RunsUserCode:
                    return "Runs user code";
                default:
                    return sideEffect.ToString();
            }
        }
    }
}
