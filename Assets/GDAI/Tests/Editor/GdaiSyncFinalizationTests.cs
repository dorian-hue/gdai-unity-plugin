// ALPHA-B B-1/B-2 EditMode tests: the finalization context latch (persist → take-once →
// refuse mismatch/corrupt) and the scoped Build Settings normalization. FinalizeSync's
// scene-touching body is exercised by the hot-project regression, not here; this suite
// pins the pure decision logic that makes finalization exactly-once and scope-safe.
using System;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerB;
using GDAI.Bridge.Editor.LayerC;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

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
            plugin_version = "0.1.0-alpha.8.10",
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
            Assert.AreEqual("0.1.0-alpha.8.10", taken.plugin_version);
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

    // ALPHA-B FIX A: background coverage must derive from the CONTRACT camera spec —
    // a pure function of snapshot data — never from Camera.main (null on a fresh sync;
    // and even when present, cam.aspect is the live Editor viewport, which made
    // Main.unity a function of the operator's window layout). Verdict tests 1-3.
    public class GdaiSceneBackgroundCoverageTests
    {
        private Texture2D _tex;
        private Sprite _sprite;      // 1024 px @ 100 PPU → 10.24 world units
        private GameObject _target;
        private readonly System.Collections.Generic.List<GameObject> _cleanup = new System.Collections.Generic.List<GameObject>();

        // The R2 regression contract's camera block.
        private static GdaiPlayableContract.CameraSpec Spec() => new GdaiPlayableContract.CameraSpec
        {
            padding_ratio = 0.1f,
            target_aspect = 16f / 9f,
            world_bounds = new GdaiPlayableContract.WorldBounds { width = 9.6f, height = 5.4f },
            position = new GdaiPlayableContract.Vec3 { x = 0f, y = 0f, z = -10f },
        };

        // coverW / spriteW = (2 · 2.97 · 16/9) / 10.24
        private const float ExpectedScale = 1.03125f;
        // fallback branch: max(20 · 16/9, 20) / 10.24
        private const float FallbackScale = 3.4722226f;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _tex = new Texture2D(1024, 1024);
            _sprite = Sprite.Create(_tex, new Rect(0, 0, 1024, 1024), new Vector2(0.5f, 0.5f), 100f);
            _target = new GameObject("bg-under-test");
            _cleanup.Add(_target);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _cleanup) if (go != null) Object.DestroyImmediate(go);
            _cleanup.Clear();
            if (_sprite != null) Object.DestroyImmediate(_sprite);
            if (_tex != null) Object.DestroyImmediate(_tex);
        }

        private float ScaleWith(GdaiPlayableContract.CameraSpec spec)
        {
            GdaiSceneBackgroundBinder.PositionAndScaleToCover(_target.transform, _sprite, spec);
            return _target.transform.localScale.x;
        }

        private Camera MakeMainCamera(float aspect)
        {
            var go = new GameObject("Main Camera");
            _cleanup.Add(go);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.97f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.aspect = aspect;
            return cam;
        }

        [Test]
        public void ContractSpec_YieldsDeterministicScale()
        {
            Assert.That(ScaleWith(Spec()), Is.EqualTo(ExpectedScale).Within(1e-4f),
                "scale must be the contract fit_arena solve, 10.56/10.24");
            Assert.AreEqual(0f, _target.transform.position.x, 1e-5f);
            Assert.AreEqual(0f, _target.transform.position.y, 1e-5f);
            Assert.AreEqual(0f, _target.transform.position.z, 1e-5f, "background sits on the gameplay plane");
        }

        [Test]
        public void ContractSpec_IsCameraAndViewportInvariant()
        {
            // no camera at all (the fresh-sync shape)
            float noCam = ScaleWith(Spec());
            // camera present with one viewport aspect
            var cam = MakeMainCamera(1.193f);
            float aspectA = ScaleWith(Spec());
            // same camera, different viewport aspect (the resize-the-Game-view shape)
            cam.aspect = 2.0f;
            float aspectB = ScaleWith(Spec());

            Assert.AreEqual(noCam, aspectA, 1e-6f, "camera presence must not change the scale");
            Assert.AreEqual(aspectA, aspectB, 1e-6f, "viewport aspect must not change the scale");
            Assert.That(noCam, Is.EqualTo(ExpectedScale).Within(1e-4f));
        }

        [Test]
        public void ContractSpec_NeverFallsBackToTwentyUnits()
        {
            Assert.That(ScaleWith(Spec()), Is.Not.EqualTo(FallbackScale).Within(1e-3f),
                "with a spec supplied the 20-unit Camera.main-null fallback must be unreachable");
        }
    }

    // ALPHA-B FIX B: the role-map writer must settle the AssetDatabase inside the same
    // call — its .meta being minted by whatever refresh runs NEXT is what broke the
    // meta-GUID idempotency gate. Verdict test 4.
    public class GdaiSemanticRoleMapSettlementTests
    {
        private string _dirAbs;
        private string _backupDir;

        [SetUp]
        public void SetUp()
        {
            _dirAbs = Path.Combine(Directory.GetCurrentDirectory(), GdaiSemanticRoleMap.RoleMapDir.Replace('/', Path.DirectorySeparatorChar));
            // preserve a real role map (project-level confirmed truth) byte-for-byte
            if (Directory.Exists(_dirAbs))
            {
                _backupDir = _dirAbs + ".test-bak";
                Directory.Move(_dirAbs, _backupDir);
                if (File.Exists(_dirAbs + ".meta")) File.Move(_dirAbs + ".meta", _backupDir + ".meta");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dirAbs)) Directory.Delete(_dirAbs, true);
            if (File.Exists(_dirAbs + ".meta")) File.Delete(_dirAbs + ".meta");
            if (_backupDir != null)
            {
                Directory.Move(_backupDir, _dirAbs);
                if (File.Exists(_backupDir + ".meta")) File.Move(_backupDir + ".meta", _dirAbs + ".meta");
                _backupDir = null;
            }
            AssetDatabase.Refresh();
        }

        [Test]
        public void WriteFromBundle_SettlesAssetDatabaseInTheSameCall()
        {
            var data = new GdaiSemanticRoleMapData
            {
                project_id = "18bedbf4-3993-422b-97ce-e5eb910bb55c",
                roles = new System.Collections.Generic.List<GdaiSemanticRoleEntry>
                {
                    new GdaiSemanticRoleEntry { role = "player", entity_id = "test-entity", source = "manual_confirmed" },
                },
            };
            Assert.IsTrue(GdaiSemanticRoleMap.WriteFromBundle(data, out string msg), msg);
            Assert.IsNotEmpty(AssetDatabase.AssetPathToGUID(GdaiSemanticRoleMap.RoleMapPath),
                "the role map must have a GUID within the SAME call — no deferred refresh");
            Assert.IsTrue(File.Exists(_dirAbs + ".meta"),
                "the raw-created parent folder's .meta must be minted by the path-scoped import");
            Assert.IsTrue(File.Exists(Path.Combine(_dirAbs, "gdai_semantic_role_map.json.meta")),
                "the json's .meta must exist immediately after the write");
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
