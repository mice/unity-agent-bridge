using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentBridgePlayModeProbeWatcherTests
    {
        [Test]
        public void TryDeleteIfExistsNonBlocking_ReturnsFalseWhileFileIsLocked()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "agent-bridge-playmode-probe-" + Guid.NewGuid() + ".tmp");
            File.WriteAllText(tempPath, "probe");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.That(AgentBridgePlayModeProbeWatcher.TryDeleteIfExistsNonBlocking(tempPath), Is.False, "Expected the non-blocking delete to fail fast while the file is locked.");
                    Assert.That(File.Exists(tempPath), Is.True, "Expected the locked file to remain in place.");
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void DeleteIfExistsWithRetry_DeletesFileAfterTemporaryLockRelease()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "agent-bridge-playmode-probe-" + Guid.NewGuid() + ".tmp");
            File.WriteAllText(tempPath, "probe");

            try
            {
                var lockReleased = new ManualResetEventSlim(false);
                var lockThread = new Thread(() =>
                {
                    using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        lockReleased.Set();
                        Thread.Sleep(300);
                    }
                });

                lockThread.Start();
                Assert.That(lockReleased.Wait(TimeSpan.FromSeconds(5)), Is.True, "Expected the temporary lock to be acquired.");

                Assert.That(AgentBridgePlayModeProbeWatcher.DeleteIfExistsWithRetry(tempPath), Is.True, "Expected the file to be deleted after the lock is released.");
                Assert.That(File.Exists(tempPath), Is.False, "Expected the temporary file to be removed.");
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
