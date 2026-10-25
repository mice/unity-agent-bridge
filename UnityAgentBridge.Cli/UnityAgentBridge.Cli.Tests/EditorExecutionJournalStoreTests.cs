using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnityMcp.AgentBridge;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class EditorExecutionJournalStoreTests
{
    private string fixtureRoot = null!;
    private string journalPath = null!;
    private EditorExecutionJournalStore store = null!;

    [TestInitialize]
    public void Initialize()
    {
        fixtureRoot = Path.Combine(Path.GetTempPath(), "AgentBridgeSchedulerRecordFixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        journalPath = Path.Combine(fixtureRoot, "scheduler.json");
        store = new EditorExecutionJournalStore(journalPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Only this test's newly allocated non-Unity record fixture is owned here.
        var ownedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AgentBridgeSchedulerRecordFixtures")) + Path.DirectorySeparatorChar;
        Assert.IsTrue(Path.GetFullPath(fixtureRoot).StartsWith(ownedParent, StringComparison.OrdinalIgnoreCase));
        Directory.Delete(fixtureRoot, recursive: true);
    }

    // TestRecord: Documentation~/test_records/AGBM_328.md
    [TestMethod, TestCategory("AGBM_328")]
    public void AtomicReplacement_ReturnsTheWholeCommittedVersion()
    {
        Assert.IsNull(store.Read());
        store.Write(new AgentExecutionJournal { ProjectId = "A" });
        store.Write(new AgentExecutionJournal { ProjectId = "A", NextSequence = 9 });
        Assert.AreEqual(9L, store.Read().NextSequence);
        Assert.IsFalse(File.Exists(journalPath + ".writing"));
        Assert.IsTrue(File.Exists(journalPath + ".initialized"));
    }

    // TestRecord: Documentation~/test_records/AGBM_329.md
    [TestMethod, TestCategory("AGBM_329")]
    public void MissingInitializedPrimary_IsAnErrorInsteadOfANewStore()
    {
        store.Write(new AgentExecutionJournal { ProjectId = "A" });
        File.Delete(journalPath);
        Assert.ThrowsException<IOException>(() => store.Read());
    }

    // TestRecord: Documentation~/test_records/AGBM_330.md
    [TestMethod, TestCategory("AGBM_330")]
    public void InterruptedFirstWrite_IsNotMistakenForPristineState()
    {
        File.WriteAllText(journalPath + ".writing", "{\"Version\":1,");
        Assert.ThrowsException<IOException>(() => store.Read());
    }

    // TestRecord: Documentation~/test_records/AGBM_331.md
    [TestMethod, TestCategory("AGBM_331")]
    public void DuplicateKeysAndTrailingContent_AreRejected()
    {
        File.WriteAllText(journalPath, "{\"Version\":1,\"Version\":2}");
        Assert.ThrowsException<Newtonsoft.Json.JsonReaderException>(() => store.Read());
        File.WriteAllText(journalPath, "{\"Version\":1} {\"Version\":2}");
        Assert.ThrowsException<Newtonsoft.Json.JsonReaderException>(() => store.Read());
    }

    // TestRecord: Documentation~/test_records/AGBM_332.md
    [TestMethod, TestCategory("AGBM_332")]
    public void EmptyMalformedAndOversizedFiles_FailClosed()
    {
        File.WriteAllText(journalPath, "");
        Assert.ThrowsException<InvalidDataException>(() => store.Read());
        File.WriteAllText(journalPath, "{\"Version\":");
        Assert.ThrowsException<Newtonsoft.Json.JsonReaderException>(() => store.Read());
        using (var file = File.OpenWrite(journalPath)) file.SetLength(EditorExecutionJournalStore.MaximumJournalBytes + 1L);
        Assert.ThrowsException<InvalidDataException>(() => store.Read());
    }

    // TestRecord: Documentation~/test_records/AGBM_333.md
    [TestMethod, TestCategory("AGBM_333")]
    public void OversizedReplacement_DoesNotDestroyPriorCommittedState()
    {
        store.Write(new AgentExecutionJournal { ProjectId = "A", NextSequence = 7 });
        var oversized = new AgentExecutionJournal { ProjectId = new string('x', EditorExecutionJournalStore.MaximumJournalBytes) };
        Assert.ThrowsException<InvalidDataException>(() => store.Write(oversized));
        Assert.AreEqual(7L, store.Read().NextSequence);
    }
}
