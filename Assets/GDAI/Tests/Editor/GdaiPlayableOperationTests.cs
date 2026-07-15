// AUTO-0Q-P2 · P2-B tests · Resumable operation state machine. Proves the
// safety invariants BEFORE any asset builder exists: no operation = no auto
// apply, resume only on matching project/token/snapshot/contract, the
// destructive replace is never repeated, and abort is safe.
using System;
using System.IO;
using GDAI.Bridge.Editor.LayerA;
using NUnit.Framework;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiPlayableOperationTests
    {
        private string _root;
        private static readonly DateTime T = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc);
        private const string PID = "18bedbf4-3993-422b-97ce-e5eb910bb55c";
        private const string SNAP = "2d874a40-f10d-4d90-a3c4-5c7ffdb7cdee";
        private const string SCHEMA = "gdai.unity.playable_contract.v1";
        private const int REV = 4;
        private const string SHA = "12af83d4bc487687c8207b48a50816a25571c5b952c7eedb3def5015f508d043";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gdai-op-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Test]
        public void NoOperation_MeansNoActiveAndNoAutoApply()
        {
            var active = GdaiPlayableOperation.LoadActive(_root, out int count);
            Assert.IsNull(active);
            Assert.AreEqual(0, count, "clean project must have no operation → composer must never auto-apply on open");
        }

        [Test]
        public void Create_PersistsUnderLibrary_NotAssets()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            var path = GdaiPlayableOperation.PathFor(_root, op.operation_id);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.Replace('\\', '/').Contains("/Library/GDAI/operations/"), "operation must live under Library, never Assets");
            var active = GdaiPlayableOperation.LoadActive(_root, out int count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(op.operation_id, active.operation_id);
        }

        [Test]
        public void Resume_VerifiesProjectTokenSnapshotContract()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.CodeImportedAwaitingReload, T);

            // happy path
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Resumable, op.CanResume(PID, SNAP, SCHEMA, REV, SHA, true, out _));
            // each mismatch is refused
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Unsafe, op.CanResume(PID, SNAP, SCHEMA, REV, SHA, false, out _), "no token");
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Unsafe, op.CanResume("other", SNAP, SCHEMA, REV, SHA, true, out _), "project mismatch");
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Unsafe, op.CanResume(PID, "changed", SCHEMA, REV, SHA, true, out _), "snapshot changed");
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Unsafe, op.CanResume(PID, SNAP, SCHEMA, 3, SHA, true, out _), "rev3 op under rev4 contract");
        }

        [Test]
        public void DestructivePhase_NotRepeatedOnResume()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.GeneratedRootReplaced, T);
            Assert.IsTrue(op.generated_root_committed, "replace commit must be recorded");
            // reload after a committed replace resumes PAST the destructive phase
            Assert.AreEqual(GdaiPlayablePhase.CodeImportedAwaitingReload, op.NextPhaseOnResume());
        }

        [Test]
        public void Reload_ResumesExactNextPhase()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.CodeImportedAwaitingReload, T);
            // fresh load from disk (simulating domain reload dropping the in-memory op)
            var reloaded = GdaiPlayableOperation.LoadActive(_root, out int count);
            Assert.AreEqual(1, count);
            Assert.AreEqual(GdaiPlayablePhase.ResumedAfterReload, reloaded.NextPhaseOnResume());
        }

        [Test]
        public void Terminal_OperationsAreNotActive()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.SceneObjectsComposed, T);
            op.Abort(_root, T, "user abort");
            var active = GdaiPlayableOperation.LoadActive(_root, out int count);
            Assert.AreEqual(0, count, "aborted op is terminal → not active → no auto resume");
            Assert.IsNull(active);
            Assert.AreEqual(GdaiPlayableOperation.ResumeVerdict.Terminal, op.CanResume(PID, SNAP, SCHEMA, REV, SHA, true, out _));
        }

        [Test]
        public void Fail_RecordsPhaseAndIsTerminal()
        {
            var op = GdaiPlayableOperation.Create(_root, PID, SNAP, SCHEMA, REV, SHA, "Assets/Scenes/Main.unity", T);
            op.Advance(_root, GdaiPlayablePhase.InputAssetReady, T);
            op.Fail(_root, T, "input asset write failed");
            Assert.AreEqual("FAILED:" + GdaiPlayablePhase.InputAssetReady, op.result);
            GdaiPlayableOperation.LoadActive(_root, out int count);
            Assert.AreEqual(0, count);
        }
    }
}
