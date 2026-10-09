using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class HeartbeatPublicationTests
    {
        private static void Publish(string path, string content) => typeof(AgentCommandPoller)
            .GetMethod("WriteAtomic", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { path, content });

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_285.md
        [Test, Category("AGB_285"), Timeout(30000)]
        public void ConcurrentPublicationNeverRemovesTheStatusTarget()
        {
            var directory = Path.Combine(Path.GetTempPath(), "HeartbeatPublication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "status.json");
            var missing = 0;
            var malformed = 0;
            var checks = 0;
            using (var stop = new CancellationTokenSource())
            {
                Publish(path, "snapshot:0");
                var reader = Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var text = new StreamReader(stream))
                            {
                                if (!text.ReadToEnd().StartsWith("snapshot:", StringComparison.Ordinal)) Interlocked.Increment(ref malformed);
                            }
                        }
                        catch (FileNotFoundException) { Interlocked.Increment(ref missing); }
                        catch (IOException) { }
                        Interlocked.Increment(ref checks);
                    }
                });
                try
                {
                    Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref checks) > 0, 5000), Is.True);
                    for (var i = 1; i <= 1000; i++) Publish(path, "snapshot:" + i);
                }
                finally
                {
                    stop.Cancel();
                    Assert.That(reader.Wait(5000), Is.True);
                    Directory.Delete(directory, true);
                }
            }
            Assert.That(missing, Is.EqualTo(0));
            Assert.That(malformed, Is.EqualTo(0));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_286.md
        [Test, Category("AGB_286")]
        public void SharingFailureRetainsThePreviousValidHeartbeat()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows sharing semantics fixture.");
            var directory = Path.Combine(Path.GetTempPath(), "HeartbeatSharing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "status.json");
            try
            {
                Publish(path, "snapshot:old");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.DoesNotThrow(() => Publish(path, "snapshot:new"));
                    Assert.That(File.Exists(path), Is.True);
                    Assert.That(File.ReadAllText(path), Is.EqualTo("snapshot:old"));
                }
                Publish(path, "snapshot:next");
                Assert.That(File.ReadAllText(path), Is.EqualTo("snapshot:next"));
                Assert.That(File.Exists(path + ".tmp"), Is.False);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
