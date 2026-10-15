// Minimal host seam for .NET contract tests. Integration verifiers use real Unity instead.
namespace UnityEngine
{
    internal static class Application { public static string dataPath = string.Empty; }
}
namespace UnityEditor
{
    internal static class EditorApplication
    {
        public static event System.Action? update;
        public static void Tick() => update?.Invoke();
    }
}
