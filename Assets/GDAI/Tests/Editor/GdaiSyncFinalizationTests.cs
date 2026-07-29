// ALPHA-B B-1/B-2 EditMode tests: the finalization context latch (persist → take-once →
// refuse mismatch/corrupt) and the scoped Build Settings normalization. FinalizeSync's
// scene-touching body is exercised by the hot-project regression, not here; this suite
// pins the pure decision logic that makes finalization exactly-once and scope-safe.
using System;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using NUnit.Framework;
using UnityEditor;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiSyncFinalizationTests
    {
        private string _root;

        private const string Project = "18bedbf4-3993-422b-97ce-e5eb910bb55c";
        private const string Snapshot = "dd31ba14-785f-4c99-85ca-f52d7dd1dd98";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gdai-finalize-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private static GdaiSyncFinalizationContext Ctx() => new GdaiSyncFinalizationContext
        {
            project_id = Project,
            snapshot_id = Snapshot,
            snapshot_revision = "coherent",
            plugin_version = "0.1.0-alpha.8.9",
            generated_file_count = 20,
            binary_asset_count = 3,
            preserved_meta_count = 7,
            backup_path = ".gdai/generated_backups/x",
            started_at = "2026-07-29T10:00:00Z",
        };

        private string PendingPath => Path.Combine(_root, "Library", "GDAI", "pending-finalize.json");

        [Test]
        public void PersistThenTake_RoundTripsAllFields_AndConsumesTheFile()
        {
            GdaiSyncFinalization.Persist(_root, Ctx());
            Assert.IsTrue(File.Exists(PendingPath), "persist must write the pending file");

            Assert.IsTrue(GdaiSyncFinalization.TryTake(_root, Project, Snapshot, out var taken));
            Assert.IsFalse(File.Exists(PendingPath), "take must delete the file BEFORE use (latch)");
            Assert.AreEqual(Project, taken.project_id);
            Assert.AreEqual(Snapshot, taken.snapshot_id);
            Assert.AreEqual("coherent", taken.snapshot_revision);
            Assert.AreEqual("0.1.0-alpha.8.9", taken.plugin_version);
            Assert.AreEqual(20, taken.generated_file_count);
            Assert.AreEqual(3, taken.binary_asset_count);
            Assert.AreEqual(7, taken.preserved_meta_count);
            Assert.AreEqual(".gdai/generated_backups/x", taken.backup_path);
            Assert.AreEqual("2026-07-29T10:00:00Z", taken.started_at);
        }

        [Test]
        public void SecondTake_ReturnsFalse_TheLatchIsExactlyOnce()
        {
            GdaiSyncFinalization.Persist(_root, Ctx());
            Assert.IsTrue(GdaiSyncFinalization.TryTake(_root, Project, Snapshot, out _));
            Assert.IsFalse(GdaiSyncFinalization.TryTake(_root, Project, Snapshot, out var second),
                "a repeated resume must never finalize twice");
            Assert.IsNull(second);
        }

        [Test]
        public void Take_RefusesProjectMismatch_AndStillConsumes()
        {
            GdaiSyncFinalization.Persist(_root, Ctx());
            Assert.IsFalse(GdaiSyncFinalization.TryTake(_root, "76173a50-72e4-4c3e-ace9-abc43348070d", Snapshot, out var ctx),
                "a context from another operation must be refused");
            Assert.IsNull(ctx);
            Assert.IsFalse(File.Exists(PendingPath), "refused context is consumed, never retried");
        }

        [Test]
        public void Take_RefusesSnapshotMismatch()
        {
            GdaiSyncFinalization.Persist(_root, Ctx());
            Assert.IsFalse(GdaiSyncFinalization.TryTake(_root, Project, "72aa9810-0000-0000-0000-000000000000", out var ctx));
            Assert.IsNull(ctx);
            Assert.IsFalse(File.Exists(PendingPath));
        }

        [Test]
        public void Take_ConsumesCorruptFile_WithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PendingPath));
            File.WriteAllText(PendingPath, "not json {{{");
            Assert.IsFalse(GdaiSyncFinalization.TryTake(_root, Project, Snapshot, out var ctx));
            Assert.IsNull(ctx);
            Assert.IsFalse(File.Exists(PendingPath), "corrupt context must be consumed, not retried forever");
        }

        [Test]
        public void Take_WithNothingPending_ReturnsFalse()
        {
            Assert.IsFalse(GdaiSyncFinalization.TryTake(_root, Project, Snapshot, out var ctx));
            Assert.IsNull(ctx);
        }

        [Test]
        public void Clear_RemovesThePendingFile()
        {
            GdaiSyncFinalization.Persist(_root, Ctx());
            GdaiSyncFinalization.Clear(_root);
            Assert.IsFalse(File.Exists(PendingPath));
        }

        [Test]
        public void FinalizeSync_NullContext_FailsClosed_NoReceipt()
        {
            var fin = GdaiSyncFinalization.FinalizeSync(_root, null, new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc));
            Assert.IsFalse(fin.Ok);
            Assert.AreEqual("FINALIZE_CONTEXT_MISSING", fin.FailPhase);
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, ".gdai", "receipts")), "no receipt on failure");
        }
    }

    // B-2: the Build Settings normalization is scoped — inside a GDAI-owned scaffold the
    // template SampleScene is disabled; user scenes are never touched; without the binding
    // marker the legacy append-and-enable behaviour is byte-for-byte preserved.
    // The suite snapshots and restores both EditorBuildSettings and the binding marker of
    // the HOST project so it leaves no trace.
    public class GdaiCanonicalSceneBuildSettingsTests
    {
        private EditorBuildSettingsScene[] _savedScenes;
        private string _bindingPath;
        private bool _bindingExisted;
        private string _bindingBackup;

        private const string Canonical = "Assets/Scenes/Main.unity";
        private const string Sample = "Assets/Scenes/SampleScene.unity";
        private const string UserScene = "Assets/Scenes/UserOwnedLevel.unity";

        [SetUp]
        public void SetUp()
        {
            _savedScenes = EditorBuildSettings.scenes;
            _bindingPath = Path.Combine(Directory.GetCurrentDirectory(),
                GdaiProjectBinding.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            _bindingExisted = File.Exists(_bindingPath);
            if (_bindingExisted) _bindingBackup = File.ReadAllText(_bindingPath);
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _savedScenes;
            if (_bindingExisted) File.WriteAllText(_bindingPath, _bindingBackup);
            else if (File.Exists(_bindingPath)) File.Delete(_bindingPath);
        }

        private void MarkGdaiScaffold()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_bindingPath));
            if (!_bindingExisted) File.WriteAllText(_bindingPath, "{\"schema_version\":\"gdai.unity_export.v1\"}");
        }

        [Test]
        public void GdaiScaffold_DisablesTemplateSampleScene_NeverUserScenes()
        {
            MarkGdaiScaffold();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Sample, true),
                new EditorBuildSettingsScene(UserScene, true),
            };

            Assert.IsTrue(GdaiCanonicalScene.AddToBuildSettings(Canonical));

            var scenes = EditorBuildSettings.scenes;
            Assert.IsTrue(scenes.Single(s => s.path == Canonical).enabled, "canonical must be enabled");
            Assert.IsFalse(scenes.Single(s => s.path == Sample).enabled, "template SampleScene must be disabled");
            Assert.IsTrue(scenes.Any(s => s.path == Sample), "SampleScene stays listed, never deleted");
            Assert.IsTrue(scenes.Single(s => s.path == UserScene).enabled, "user scenes are NEVER touched");
            Assert.AreEqual(1, scenes.Count(s => s.enabled && s.path != UserScene),
                "within GDAI-owned entries the canonical scene is the only enabled one");
        }

        [Test]
        public void GdaiScaffold_IsIdempotent_SecondCallChangesNothing()
        {
            MarkGdaiScaffold();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Sample, true) };
            Assert.IsTrue(GdaiCanonicalScene.AddToBuildSettings(Canonical));
            var first = EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled).ToArray();
            Assert.IsTrue(GdaiCanonicalScene.AddToBuildSettings(Canonical));
            var second = EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled).ToArray();
            CollectionAssert.AreEqual(first, second, "re-registration must be a no-op");
        }

        // B-1 amendment: the resume path re-places the scene background from the
        // persisted pointer. Without a pointer it must decline cleanly (no throw,
        // no scene mutation) — the fresh-sync no-background case.
        [Test]
        public void BackgroundReplay_NoPointer_DeclinesCleanly()
        {
            string ptr = Path.Combine(Directory.GetCurrentDirectory(),
                GDAI.Bridge.Editor.LayerB.GdaiSceneBackgroundBinder.PointerPath.Replace('/', Path.DirectorySeparatorChar));
            string backup = null;
            if (File.Exists(ptr)) { backup = ptr + ".test-bak"; File.Move(ptr, backup); }
            try
            {
                Assert.IsFalse(GDAI.Bridge.Editor.LayerB.GdaiSceneBackgroundBinder.TryPlaceFromPointer(out var msg));
                StringAssert.Contains("no background pointer", msg);
            }
            finally
            {
                if (backup != null) File.Move(backup, ptr);
            }
        }

        [Test]
        public void NonGdaiProject_KeepsLegacyAppendAndEnable_SampleSceneStaysEnabled()
        {
            // The legacy branch must EXECUTE even in a binding-marked host (a skip is not
            // a verification): SetUp already backed up the binding content, so remove the
            // marker for the duration of this test — TearDown restores it byte-for-byte.
            if (File.Exists(_bindingPath)) File.Delete(_bindingPath);
            Assert.IsFalse(File.Exists(_bindingPath), "binding marker must be absent for the legacy-path case");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Sample, true) };

            Assert.IsTrue(GdaiCanonicalScene.AddToBuildSettings(Canonical));

            var scenes = EditorBuildSettings.scenes;
            Assert.IsTrue(scenes.Single(s => s.path == Canonical).enabled);
            Assert.IsTrue(scenes.Single(s => s.path == Sample).enabled,
                "outside a GDAI scaffold nothing is ever disabled (pre-B-2 behaviour preserved)");
        }
    }
}
