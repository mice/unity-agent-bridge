using Newtonsoft.Json;

namespace UnityAgentBridge.RoslynCompiler;

internal static class CompileProtocolJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None
    };

    public static CompileRequest DeserializeRequest(string json)
    {
        var request = JsonConvert.DeserializeObject<CompileRequest>(json, Settings);
        if (request == null)
        {
            throw new InvalidOperationException("Compile request payload deserialized to null.");
        }

        return request;
    }

    public static string SerializeResponse(CompileResponse response)
    {
        return JsonConvert.SerializeObject(response, Settings);
    }
}
