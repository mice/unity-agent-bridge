using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class AgentBridgePlayModeProbeWatcher
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static string _projectRoot;
        private static string _enterTriggerPath;
        private static string _exitTriggerPath;
        private static string _enteredMarkerPath;
        private static string _exitedMarkerPath;

        static AgentBridgePlayModeProbeWatcher()
        {
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_projectRoot))
            {
                return;
            }

            var tempRoot = Path.Combine(_projectRoot, "Temp", "AgentBridge");
            _enterTriggerPath = Path.Combine(tempRoot, "playmode-probe.enter");
            _exitTriggerPath = Path.Combine(tempRoot, "playmode-probe.exit");
            _enteredMarkerPath = Path.Combine(tempRoot, "playmode-probe.entered");
            _exitedMarkerPath = Path.Combine(tempRoot, "playmode-probe.exited");

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnEditorUpdate()
        {
            if (!string.IsNullOrWhiteSpace(_enterTriggerPath) && File.Exists(_enterTriggerPath) && !EditorApplication.isPlaying)
            {
                if (!TryDeleteIfExistsNonBlocking(_enterTriggerPath) || !TryDeleteIfExistsNonBlocking(_enteredMarkerPath))
                {
                    return;
                }

                EditorApplication.isPlaying = true;
            }

            if (!string.IsNullOrWhiteSpace(_exitTriggerPath) && File.Exists(_exitTriggerPath) && EditorApplication.isPlaying)
            {
                if (!TryDeleteIfExistsNonBlocking(_exitTriggerPath) || !TryDeleteIfExistsNonBlocking(_exitedMarkerPath))
                {
                    return;
                }

                EditorApplication.isPlaying = false;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                Touch(_enteredMarkerPath);
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                Touch(_exitedMarkerPath);
            }
        }

        private static void Touch(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            WriteAllTextOverwrite(path, "ok");
        }

        // Probe markers are verification-only files. Overwrite in place to avoid
        // the queue-style delete-then-move pattern used by formal bridge I/O.
        private static void WriteAllTextOverwrite(string targetPath, string content)
        {
            using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(content);
            }
        }

        private static void DeleteIfExists(string path)
        {
            DeleteIfExistsWithRetry(path);
        }

        // Runtime probe polling runs on the Editor main thread. Use a single, non-blocking
        // delete attempt and let the next update tick retry if a temporary file lock exists.
        internal static bool TryDeleteIfExistsNonBlocking(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return true;
            }

            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static bool DeleteIfExistsWithRetry(string path, int attempts = 20, int delayMs = 50)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return true;
            }

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return true;
                    }

                    File.Delete(path);
                    return !File.Exists(path);
                }
                catch (IOException) when (attempt < attempts - 1)
                {
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < attempts - 1)
                {
                    Thread.Sleep(delayMs);
                }
            }

            return !File.Exists(path);
        }
    }
}
