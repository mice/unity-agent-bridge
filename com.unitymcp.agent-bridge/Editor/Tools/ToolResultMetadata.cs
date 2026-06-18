using System;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class ToolResultDetailsMetadata
    {
        public bool available;
        public string reportPath;
        public bool recommendedRead;
        public string[] recommendedPointers = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ToolFollowUpOption
    {
        public string tool;
        public string reason;
        public JObject args = new JObject();
    }

    [Serializable]
    public sealed class ToolFollowUpMetadata
    {
        public bool recommended;
        public ToolFollowUpOption[] options = Array.Empty<ToolFollowUpOption>();
    }

    internal static class ToolResultMetadata
    {
        public static ToolResultDetailsMetadata CreateDetails(bool available, bool recommendedRead, params string[] recommendedPointers)
        {
            return new ToolResultDetailsMetadata
            {
                available = available,
                recommendedRead = recommendedRead,
                recommendedPointers = recommendedPointers ?? Array.Empty<string>()
            };
        }

        public static ToolFollowUpMetadata None()
        {
            return new ToolFollowUpMetadata
            {
                recommended = false,
                options = Array.Empty<ToolFollowUpOption>()
            };
        }

        public static ToolFollowUpMetadata Recommended(params ToolFollowUpOption[] options)
        {
            if (options == null || options.Length == 0 || options.Length > 3)
            {
                throw new ArgumentException("followUp options must contain between one and three entries when recommended is true.", nameof(options));
            }

            return new ToolFollowUpMetadata
            {
                recommended = true,
                options = options
            };
        }

        public static ToolFollowUpOption Option(string tool, string reason, JObject args = null)
        {
            if (string.IsNullOrWhiteSpace(tool))
            {
                throw new ArgumentException("tool is required.", nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("reason is required.", nameof(reason));
            }

            return new ToolFollowUpOption
            {
                tool = tool,
                reason = reason,
                args = args ?? new JObject()
            };
        }

        public static void AttachReportPath(ToolResultDetailsMetadata details, string reportPath)
        {
            if (details == null)
            {
                return;
            }

            details.reportPath = reportPath;
        }
    }
}
