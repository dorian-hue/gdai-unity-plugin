// T4 0J · manifest additive-merge idempotency tests (M1–M7).
// The playable composer's GdaiPlayableOwnershipManifest.Write must PRESERVE the additive v2 animation_assets
// section owned by the animation materializer, so a second sync does not orphan it (→ GUARD_UNRECORDED_OWNED
// _STAMP). Single authority manifest; preserve verbatim on SAME identity + valid; fail-closed (no write) on
// identity mismatch or malformed section.
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using GDAI.Bridge.Editor.LayerC.Animation;

namespace GDAI.Tests.Editor
{
    public class GdaiManifestMergeTests
    {
        const string PID = "18bedbf4-3993-422b-97ce-e5eb910bb55c";
        const string SNAP = "2d874a40-f10d-4d90-a3c4-5c7ffdb7cdee";
        const string SHA = "12af83d4bc487687c8207b48a50816a25571c5b952c7eedb3def5015f508d043";
        const string ScenePath = "Assets/Scenes/Main.unity";
        GdaiPlayableContract _contract;

        static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }
        static string ManifestAbs => Path.GetFullPath(GdaiPlayableOwnershipManifest.ManifestPath);

        static void Clean()
        {
            if (AssetDatabase.IsValidFolder("Assets/GDAI_Project")) AssetDatabase.DeleteAsset("Assets/GDAI_Project");
            AssetDatabase.Refresh();
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "fixture must parse");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Clean();
        }

        [TearDown]
        public void TearDown() => Clean();

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────
        static GdaiAnimationAssetsSection ValidSection(string snapshotId) => new GdaiAnimationAssetsSection
        {
            materialization = new GdaiAnimMaterializationPin
            {
                package_schema = "gdai.animation.materialization_package.v1", package_class = "TEST_ONLY",
                package_id = "TESTONLY-norm-ronin-4x6-v1", animation_profile_id = "JASON_RONIN_4X6_EXPERIMENT_V1",
                entity_id = "ronin", snapshot_id = snapshotId, package_content_sha256 = "abc123",
            },
            raw_sheets = { new GdaiAnimSheetRecord { path = "Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png", guid = "ec5a9cc7e4a974b57b6bb313302d151f", content_fingerprint = "deadbeef" } },
        };

        // seed a prior manifest at ManifestPath with the given identity + optional animation section.
        void SeedManifest(string project, string snapshot, int contractRevision, string sha, GdaiAnimationAssetsSection section, bool v2)
        {
            var m = new GdaiPlayableAssetsManifest
            {
                schema_version = v2 ? GdaiPlayableAssetsManifest.SchemaVersionV2 : GdaiPlayableAssetsManifest.SchemaVersion,
                project_id = project, profile_id = _contract.profile_id, snapshot_id = snapshot,
                contract_revision = contractRevision, contract_sha256 = sha, generated_at = "2026-07-14T00:00:00Z",
                animation_assets = section,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ManifestAbs));
            File.WriteAllText(ManifestAbs, JsonConvert.SerializeObject(m, Formatting.Indented));
        }

        static GdaiPlayableAssetsManifest ReadBack() => JsonConvert.DeserializeObject<GdaiPlayableAssetsManifest>(File.ReadAllText(ManifestAbs));

        // ── M1 · no prior manifest → plain v1 write ──
        [Test]
        public void M1_NoPrior_WritesV1_NoAnimationSection()
        {
            if (File.Exists(ManifestAbs)) File.Delete(ManifestAbs);
            Assert.IsTrue(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err), err);
            var m = ReadBack();
            Assert.AreEqual(GdaiPlayableAssetsManifest.SchemaVersion, m.schema_version);
            Assert.IsNull(m.animation_assets);
        }

        // ── M2 · prior v1 (no animation section) → still v1, section stays absent ──
        [Test]
        public void M2_PriorV1_AnimationSectionStaysAbsent()
        {
            SeedManifest(PID, SNAP, _contract.contract_revision, SHA, null, v2: false);
            Assert.IsTrue(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err), err);
            var m = ReadBack();
            Assert.AreEqual(GdaiPlayableAssetsManifest.SchemaVersion, m.schema_version);
            Assert.IsNull(m.animation_assets);
        }

        // ── M3 · valid prior v2, SAME identity → playable fields updated + animation_assets preserved + v2 ──
        [Test]
        public void M3_ValidPriorV2_SameIdentity_PreservesAnimationAssets()
        {
            SeedManifest(PID, SNAP, _contract.contract_revision, SHA, ValidSection(SNAP), v2: true);
            Assert.IsTrue(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err), err);
            var m = ReadBack();
            Assert.AreEqual(GdaiPlayableAssetsManifest.SchemaVersionV2, m.schema_version, "schema stays v2");
            Assert.IsNotNull(m.animation_assets, "animation_assets preserved");
            Assert.AreEqual("TESTONLY-norm-ronin-4x6-v1", m.animation_assets.materialization.package_id);
            Assert.AreEqual(1, m.animation_assets.raw_sheets.Count);
            Assert.AreEqual("ec5a9cc7e4a974b57b6bb313302d151f", m.animation_assets.raw_sheets[0].guid, "sheet GUID stable");
            // playable v1 fields were re-derived (composer owns them)
            Assert.AreEqual(PID, m.project_id);
            Assert.AreEqual(_contract.profile_id, m.profile_id);
        }

        // ── M4 · prior v2 with DIFFERENT project → fail closed, bytes unchanged ──
        [Test]
        public void M4_ProjectMismatch_FailsClosed_BytesUnchanged()
        {
            SeedManifest("00000000-0000-4000-8000-000000000000", SNAP, _contract.contract_revision, SHA, ValidSection(SNAP), v2: true);
            byte[] before = File.ReadAllBytes(ManifestAbs);
            Assert.IsFalse(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err));
            StringAssert.Contains("MANIFEST_PRESERVE_IDENTITY_MISMATCH", err);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ManifestAbs), "manifest bytes unchanged on fail-closed");
        }

        // ── M5 · prior v2 with DIFFERENT snapshot → fail closed, bytes unchanged ──
        [Test]
        public void M5_SnapshotMismatch_FailsClosed_BytesUnchanged()
        {
            SeedManifest(PID, "ffffffff-ffff-4fff-8fff-ffffffffffff", _contract.contract_revision, SHA, ValidSection("ffffffff-ffff-4fff-8fff-ffffffffffff"), v2: true);
            byte[] before = File.ReadAllBytes(ManifestAbs);
            Assert.IsFalse(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err));
            StringAssert.Contains("MANIFEST_PRESERVE_IDENTITY_MISMATCH", err);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ManifestAbs), "bytes unchanged");
        }

        // ── M6 · prior v2 with malformed animation_assets → fail closed, NOT converted to empty ──
        [Test]
        public void M6_MalformedAnimationSection_FailsClosed_BytesUnchanged()
        {
            var broken = ValidSection(SNAP);
            broken.materialization = null; // present-but-broken section
            SeedManifest(PID, SNAP, _contract.contract_revision, SHA, broken, v2: true);
            byte[] before = File.ReadAllBytes(ManifestAbs);
            Assert.IsFalse(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err));
            StringAssert.Contains("MANIFEST_PRESERVE_MALFORMED_ANIMATION_SECTION", err);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ManifestAbs), "malformed section is not silently emptied");
        }

        // ── M7 · preservation is VERBATIM (no fingerprint recompute) → human-edit protection untouched ──
        [Test]
        public void M7_PreservationIsVerbatim_FingerprintsUntouched()
        {
            var section = ValidSection(SNAP);
            string sectionJsonBefore = JsonConvert.SerializeObject(section);
            SeedManifest(PID, SNAP, _contract.contract_revision, SHA, section, v2: true);
            Assert.IsTrue(GdaiPlayableOwnershipManifest.Write(_contract, PID, SNAP, SHA, ScenePath, out string err), err);
            var m = ReadBack();
            // the animation_assets section round-trips byte-for-byte (content_fingerprint + guids unchanged),
            // so the materializer's GUARD_FINGERPRINT_DRIFT human-edit protection (which recomputes SHAs and
            // compares to these recorded fingerprints — unchanged code) still enforces on the next materialize.
            Assert.AreEqual(sectionJsonBefore, JsonConvert.SerializeObject(m.animation_assets), "animation_assets preserved verbatim");
            Assert.AreEqual("deadbeef", m.animation_assets.raw_sheets[0].content_fingerprint, "recorded fingerprint untouched");
        }
    }
}
