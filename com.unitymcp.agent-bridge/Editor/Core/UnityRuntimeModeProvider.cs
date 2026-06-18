using UnityEditor;

namespace UnityMcp.AgentBridge
{
    public enum UnityRuntimeMode
    {
        EditMode,
        PlayMode,
        EnteringPlayMode,
        ExitingPlayMode
    }

    public interface IUnityRuntimeModeProvider
    {
        UnityRuntimeMode GetCurrentMode();
    }

    [InitializeOnLoad]
    internal static class UnityEditorRuntimeModeTracker
    {
        private static UnityRuntimeMode _currentMode;

        static UnityEditorRuntimeModeTracker()
        {
            _currentMode = EditorApplication.isPlaying
                ? UnityRuntimeMode.PlayMode
                : (EditorApplication.isPlayingOrWillChangePlaymode ? UnityRuntimeMode.EnteringPlayMode : UnityRuntimeMode.EditMode);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static UnityRuntimeMode GetCurrentMode()
        {
            if (EditorApplication.isPlaying)
            {
                if (_currentMode == UnityRuntimeMode.EnteringPlayMode)
                {
                    _currentMode = UnityRuntimeMode.PlayMode;
                }

                return _currentMode == UnityRuntimeMode.ExitingPlayMode ? UnityRuntimeMode.ExitingPlayMode : UnityRuntimeMode.PlayMode;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return _currentMode == UnityRuntimeMode.ExitingPlayMode
                    ? UnityRuntimeMode.ExitingPlayMode
                    : UnityRuntimeMode.EnteringPlayMode;
            }

            _currentMode = UnityRuntimeMode.EditMode;
            return _currentMode;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    _currentMode = UnityRuntimeMode.EnteringPlayMode;
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    _currentMode = UnityRuntimeMode.PlayMode;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    _currentMode = UnityRuntimeMode.ExitingPlayMode;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    _currentMode = UnityRuntimeMode.EditMode;
                    break;
            }
        }
    }

    internal sealed class UnityEditorRuntimeModeProvider : IUnityRuntimeModeProvider
    {
        internal static readonly UnityEditorRuntimeModeProvider Instance = new UnityEditorRuntimeModeProvider();

        private UnityEditorRuntimeModeProvider()
        {
        }

        public UnityRuntimeMode GetCurrentMode()
        {
            return UnityEditorRuntimeModeTracker.GetCurrentMode();
        }
    }
}
