// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · EditMode matrix (0E-07 / Gate-A Phase 7).
//
// Covers: guard-is-sole-write-precondition · foreign/stamped/path-escape refusals ·
// package/JCS/sheet hash failures · Option-B fixture byte+pixel proof · two differently-
// shaped packages through ONE importer · sprite_name verbatim · Manifest v2 compat +
// animation_profile_id namespace · dedicated label · file_id string · per-kind fingerprints ·
// Verify forward/reverse · receipt non-vacuity · identical second sync (GUID/fileID stability,
// clip-ref survival, zero duplicates) · plan.removals empty · no DeleteAsset reachable.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerC;
using GDAI.Bridge.Editor.LayerC.Animation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiAnimationStage1ATests
    {
        const string FixtureRel = "Assets/GDAI/Tests/Editor/Fixtures/Animation";
        const string RoninPng = "TESTONLY-ronin-4x6-64.png";
        const string SkeletonPng = "TESTONLY-skeleton-4x5-48.png";
        const string RoninSha = "9d6a95bf61b6f7a7fe9db29d15ec0254fa80169085c3256fd5aeae1ef66fb049";
        const string SkeletonSha = "4d7833a7d26aef98e329c261b96ad47b51b531844bd35e9638e65d45c9ebdf77";
        const string RoninPkgSha = "a555c21c525f049f860ce105bee1bb84d1d0c7c7b1356554b7a88c223a590252";     // Deno golden
        const string SkeletonPkgSha = "caff803254f431c6f545484c457746c5d0f8404df34574673ec36f06c88b555d";  // Deno golden
        const string GenRoot = "Assets/GDAI_Project";
        const string Snapshot = "TESTONLY-snap-0f";

        static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        static string FixtureAbs(string file)
        {
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/ai.gamedevs.plugin/package.json");
            string root = pi != null ? Path.Combine(pi.resolvedPath, FixtureRel) : Path.GetFullPath(Path.Combine(ProjectRoot(), FixtureRel));
            return Path.Combine(root, file);
        }

        static string LoadPackageJson(string file) => File.ReadAllText(FixtureAbs(file));

        static void WriteBaselineV1Manifest()
        {
            string dir = Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Manifests");
            Directory.CreateDirectory(dir);
            var v1 = new JObject
            {
                ["schema_version"] = "gdai.unity.playable_assets.v1",
                ["project_id"] = "TESTONLY-project-0f",
                ["profile_id"] = "unity.pointer_action_demo.v1",   // PLAYABLE namespace (≠ animation_profile_id)
                ["snapshot_id"] = Snapshot,
                ["contract_revision"] = 4,
                ["contract_sha256"] = new string('0', 64),
                ["generated_at"] = "2026-07-14T00:00:00.0000000Z",
                ["input_asset"] = new JObject { ["kind"] = "input", ["path"] = "X", ["guid"] = "g" },
                ["enemy_prefab"] = new JObject { ["kind"] = "enemy_prefab", ["path"] = "X", ["guid"] = "g" },
                ["canonical_scene"] = new JObject { ["kind"] = "canonical_scene", ["path"] = "X", ["guid"] = "g" },
                ["assets"] = new JArray(),
                ["owned_scene_objects"] = new JArray(new JObject { ["name"] = "T", ["role"] = "t", ["profile_id"] = "unity.pointer_action_demo.v1", ["snapshot_id"] = Snapshot }),
            };
            File.WriteAllText(Path.Combine(dir, "GDAIPlayableAssets.json"), v1.ToString(Newtonsoft.Json.Formatting.Indented));
            AssetDatabase.Refresh();
        }

        [SetUp]
        public void Clean()
        {
            if (Directory.Exists(Path.Combine(ProjectRoot(), GenRoot)))
            {
                FileUtil.DeleteFileOrDirectory(GenRoot);
                FileUtil.DeleteFileOrDirectory(GenRoot + ".meta");
                AssetDatabase.Refresh();
            }
        }

        static GdaiAnimMaterializeResult RunRonin() =>
            GdaiAnimationMaterializer.Run(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"), FixtureAbs(RoninPng), "TEST_ONLY");
        static GdaiAnimMaterializeResult RunSkeleton() =>
            GdaiAnimationMaterializer.Run(LoadPackageJson("TESTONLY-norm-skeleton-4x5-v1.json"), FixtureAbs(SkeletonPng), "TEST_ONLY");

        // ── RFC 8785 cross-language vectors (must match jcs.ts byte-for-byte) ──
        [Test]
        public void Jcs_CrossLanguage_GoldenVectors()
        {
            Assert.AreEqual("{\"a\":1,\"b\":2}", GdaiAnimJson.Canonicalize(JObject.Parse("{\"b\":2,\"a\":1}")));
            Assert.AreEqual("{\"a\":[1,2,0.5],\"m\":{\"x\":\"s\",\"y\":false},\"z\":null}",
                GdaiAnimJson.Canonicalize(JObject.Parse("{\"z\":null,\"a\":[1,2,0.5],\"m\":{\"y\":false,\"x\":\"s\"}}")));
            Assert.AreEqual("{\"big\":1234567890,\"half\":0.5,\"n\":64,\"neg\":-12}",
                GdaiAnimJson.Canonicalize(JObject.Parse("{\"n\":64,\"half\":0.5,\"neg\":-12,\"big\":1234567890}")));
            Assert.AreEqual("43258cff783fe7036d8a43033f830adfc60ec037382473548ac742b888292777",
                GdaiAnimJson.JcsSha256Hex(JObject.Parse("{\"b\":2,\"a\":1}")));
        }

        [Test]
        public void Jcs_PackageHashes_MatchDenoGoldens_AndValidate()
        {
            var ronin = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            Assert.AreEqual(RoninPkgSha, GdaiAnimJson.PackageContentSha256(ronin.Raw), "C# JCS must reproduce the Deno package hash");
            ronin.ValidateForMaterialization("TEST_ONLY");
            var skel = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-skeleton-4x5-v1.json"));
            Assert.AreEqual(SkeletonPkgSha, GdaiAnimJson.PackageContentSha256(skel.Raw));
            skel.ValidateForMaterialization("TEST_ONLY");
        }

        // ── Phase 2: canonical machine-readable schema authority (cross-repo, byte-identical) ──
        const string CanonicalSchemaSha = "c21fcd98bda3f30b9057774563374fb60838cde102163926a7274627593de9f8";

        [Test]
        public void Schema_CanonicalFileSha_IsPinned_CrossRepoAuthority()
        {
            Assert.AreEqual(CanonicalSchemaSha, GdaiAnimSchemaValidator.SchemaFileSha256(),
                "Plugin schema file drifted from the pinned cross-repo SHA (must equal the Flowcraft mirror)");
        }

        [Test]
        public void Schema_IncomingFixturePackages_ValidateAgainstCanonicalSchema_BothShapes()
        {
            var schema = GdaiAnimSchemaValidator.LoadSchema();
            foreach (var f in new[] { "TESTONLY-norm-ronin-4x6-v1.json", "TESTONLY-norm-skeleton-4x5-v1.json" })
            {
                var pkg = GdaiAnimJson.ParseObject(LoadPackageJson(f));
                var errs = GdaiAnimSchemaValidator.Validate(pkg, schema);
                Assert.IsEmpty(errs, f + " must satisfy the canonical schema: " + string.Join("; ", errs));
            }
        }

        [Test]
        public void Schema_Drift_DeterministicFailure()
        {
            var schema = GdaiAnimSchemaValidator.LoadSchema();
            var pkg = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            var extra = (JObject)pkg.DeepClone(); extra["unexpected_field"] = 1;
            Assert.IsTrue(GdaiAnimSchemaValidator.Validate(extra, schema).Any(e => e.Contains("additional property 'unexpected_field'")));
            var badClass = (JObject)pkg.DeepClone(); badClass["package_class"] = "SANDBOX";
            Assert.IsTrue(GdaiAnimSchemaValidator.Validate(badClass, schema).Any(e => e.Contains("enum")));
            var badName = (JObject)pkg.DeepClone(); ((JArray)badName["cells"])[0]["sprite_name"] = "not_owned";
            Assert.IsTrue(GdaiAnimSchemaValidator.Validate(badName, schema).Any(e => e.Contains("pattern")));
            var badRoot = (JObject)pkg.DeepClone(); badRoot["materialization_target"]["root"] = "Assets/Elsewhere";
            Assert.IsTrue(GdaiAnimSchemaValidator.Validate(badRoot, schema).Any(e => e.Contains("const")));
        }

        // ── Option B fixture proof: byte SHA + decoded pixel semantics + failure codes ──
        [Test]
        public void Fixture_GoldenByteAndPixelSemantics_BothShapes_Pass()
        {
            var ronin = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            Assert.IsTrue(GdaiAnimationFixture.Verify(FixtureAbs(RoninPng), 393698, RoninSha, 256, 384, ronin, out var e1), e1);
            var skel = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-skeleton-4x5-v1.json"));
            Assert.IsTrue(GdaiAnimationFixture.Verify(FixtureAbs(SkeletonPng), 184643, SkeletonSha, 192, 240, skel, out var e2), e2);
        }

        [Test]
        public void Fixture_FailureCodes_FailClosed()
        {
            var ronin = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            string tmp = Path.Combine(ProjectRoot(), "Temp", "t4-tampered.png");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp));

            var bytes = File.ReadAllBytes(FixtureAbs(RoninPng));
            bytes[bytes.Length - 20] ^= 0xFF;
            File.WriteAllBytes(tmp, bytes);
            Assert.IsFalse(GdaiAnimationFixture.Verify(tmp, 393698, RoninSha, 256, 384, ronin, out var e));
            StringAssert.StartsWith("FIXTURE_FILE_HASH_DRIFT", e);

            File.WriteAllBytes(tmp, bytes.Take(1000).ToArray());
            Assert.IsFalse(GdaiAnimationFixture.Verify(tmp, 393698, RoninSha, 256, 384, ronin, out e));
            StringAssert.StartsWith("FIXTURE_FILE_SIZE_DRIFT", e);

            Assert.IsFalse(GdaiAnimationFixture.Verify(FixtureAbs(RoninPng), 393698, RoninSha, 999, 384, ronin, out e));
            StringAssert.StartsWith("FIXTURE_DIMENSION_MISMATCH", e);

            // same bytes+dims, package with a DIFFERENT cell grid that still divides 256x384 →
            // gutters land elsewhere → the decoded-pixel semantic check must fire.
            var wrongGrid = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            wrongGrid.cell_width = 128; wrongGrid.cell_height = 128; wrongGrid.columns = 2; wrongGrid.rows = 3;
            Assert.IsFalse(GdaiAnimationFixture.Verify(FixtureAbs(RoninPng), 393698, RoninSha, 256, 384, wrongGrid, out e));
            StringAssert.StartsWith("FIXTURE_PIXEL_SEMANTICS_DRIFT", e);

            string notPng = Path.Combine(ProjectRoot(), "Temp", "t4-notpng.png");
            File.WriteAllText(notPng, "{\"not\":\"png\"}");
            long len = new FileInfo(notPng).Length;
            string sha = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(notPng));
            Assert.IsFalse(GdaiAnimationFixture.Verify(notPng, len, sha, 256, 384, ronin, out e));
            StringAssert.StartsWith("FIXTURE_PNG_DECODE_FAILED", e);
        }

        // ── hash gates (no bypass) ──
        [Test]
        public void PackageHash_Tampered_RefusesBeforeAnything()
        {
            var o = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            o["entity_id"] = "ronin_tampered"; // content changed; embedded hash now stale
            var ex = Assert.Throws<GdaiAnimGateException>(() => GdaiAnimationPackage.Parse(o.ToString()).ValidateStructure());
            Assert.AreEqual("HASH_PACKAGE_CONTENT_MISMATCH", ex.Code);
        }

        [Test]
        public void SheetHash_Mismatch_RefusesBeforeGuard()
        {
            WriteBaselineV1Manifest();
            // ronin package + skeleton png → HASH_SHEET_MISMATCH, zero writes
            var r = GdaiAnimationMaterializer.Run(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"), FixtureAbs(SkeletonPng), "TEST_ONLY");
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("HASH_SHEET_MISMATCH", r.error);
            Assert.IsFalse(Directory.Exists(Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Animations")), "no writes on refusal");
        }

        // ── class gates ──
        [Test]
        public void Gates_ProductionRejectsTestOnly_AndLicenseGate()
        {
            var p = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            var ex = Assert.Throws<GdaiAnimGateException>(() => p.ValidateForMaterialization("PRODUCTION"));
            Assert.AreEqual("GATE_PROD_REJECTS_TESTONLY", ex.Code);

            // flip to PRODUCTION (markers cleaned) with license UNKNOWN → license gate
            var o = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            o["package_class"] = "PRODUCTION";
            o["package_id"] = "norm-ronin-4x6-v1";
            o["adoption"]["adoption_event_id"] = "adopt-prod-001";
            o["qa"]["qa_event_id"] = "qa-prod-001";
            o["package_content_sha256"] = "";
            o["package_content_sha256"] = GdaiAnimJson.PackageContentSha256(o);
            var prod = GdaiAnimationPackage.Parse(o.ToString());
            ex = Assert.Throws<GdaiAnimGateException>(() => prod.ValidateForMaterialization("PRODUCTION"));
            Assert.AreEqual("GATE_PROD_LICENSE_UNCLEARED", ex.Code);

            // marker disagreement: PRODUCTION class carrying TESTONLY markers
            var bad = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            bad["package_class"] = "PRODUCTION";
            bad["package_content_sha256"] = "";
            bad["package_content_sha256"] = GdaiAnimJson.PackageContentSha256(bad);
            ex = Assert.Throws<GdaiAnimGateException>(() => GdaiAnimationPackage.Parse(bad.ToString()).ValidateForMaterialization("PRODUCTION"));
            Assert.AreEqual("GATE_CLASS_MARKER_DISAGREEMENT:production_with_test_markers", ex.Code);
        }

        // ── guard: sole write precondition + refusal evidence (writes 0) ──
        [Test]
        public void Guard_AbsentManifest_RefusesWithZeroWrites()
        {
            var r = RunRonin(); // no baseline manifest written
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GUARD_MANIFEST_LOAD_FAILED", r.error);
            string root = Path.Combine(ProjectRoot(), "Assets/GDAI_Project");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Generated/Animations")), "Assets writes = 0");
            Assert.IsFalse(File.Exists(Path.Combine(root, "Generated/Manifests/GDAIPlayableAssets.json")), "manifest writes = 0");
            Assert.IsFalse(File.Exists(Path.Combine(root, "Generated/Manifests/GDAIAnimationReceipt.json")), "product receipt writes = 0");
            // failure evidence exists ONLY under Library/GDAI/operations
            var ops = Directory.GetFiles(Path.Combine(ProjectRoot(), "Library/GDAI/operations"), "anim-guard-*.json");
            Assert.IsTrue(ops.Length > 0, "Library refusal record present");
        }

        [Test]
        public void Guard_ForeignTarget_Refuses_AndNeverTouchesHumanFile()
        {
            WriteBaselineV1Manifest();
            // a HUMAN asset sits exactly at the sheet target path (no label, no record)
            string sheetPath = "Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png";
            string abs = Path.Combine(ProjectRoot(), sheetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            byte[] human = File.ReadAllBytes(FixtureAbs(SkeletonPng)); // any bytes ≠ package sheet
            File.WriteAllBytes(abs, human);
            AssetDatabase.Refresh();

            var r = RunRonin();
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GUARD_TARGET_OCCUPIED_FOREIGN", r.error);
            CollectionAssert.AreEqual(human, File.ReadAllBytes(abs), "human file byte-identical after refusal");
        }

        [Test]
        public void Guard_StampedButUnrecorded_Refuses_FlagNeverOverwrite()
        {
            WriteBaselineV1Manifest();
            string sheetPath = "Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png";
            string abs = Path.Combine(ProjectRoot(), sheetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            byte[] stampedBytes = File.ReadAllBytes(FixtureAbs(SkeletonPng));
            File.WriteAllBytes(abs, stampedBytes);
            AssetDatabase.Refresh();
            var obj = AssetDatabase.LoadMainAssetAtPath(sheetPath);
            AssetDatabase.SetLabels(obj, new[] { "GDAI_Owned_Animation" }); // stamp WITHOUT a manifest record

            var r = RunRonin();
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GUARD_UNRECORDED_OWNED_STAMP", r.error);
            CollectionAssert.AreEqual(stampedBytes, File.ReadAllBytes(abs), "stamped-but-unrecorded asset untouched");
        }

        [Test]
        public void Guard_PathEscape_And_UnplannedWrite_Refuse()
        {
            WriteBaselineV1Manifest();
            var p = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            var plan = GdaiPreMutationGuard.BuildFixedSetPlan(p);
            plan.Add(GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Clip, "Assets/Scripts/Evil.anim"));
            Assert.IsFalse(GdaiPreMutationGuard.Evaluate(p, "TEST_ONLY", plan, new List<string>(), out _, out var code));
            StringAssert.StartsWith("GUARD_PATH_OUT_OF_SCOPE", code);

            // approved plan is immutable: an unplanned write path throws before any mutation
            var goodPlan = GdaiPreMutationGuard.BuildFixedSetPlan(p);
            Assert.IsTrue(GdaiPreMutationGuard.Evaluate(p, "TEST_ONLY", goodPlan, new List<string>(), out var approval, out _));
            var ex = Assert.Throws<GdaiAnimGateException>(() => approval.AssertInPlan("Assets/Scripts/Evil.anim"));
            StringAssert.StartsWith("STOP_WRITE_NOT_IN_APPROVED_PLAN", ex.Code);
        }

        [Test]
        public void Guard_NonEmptyRemovals_IsStage1AScopeBreach()
        {
            WriteBaselineV1Manifest();
            var p = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            var plan = GdaiPreMutationGuard.BuildFixedSetPlan(p);
            Assert.IsFalse(GdaiPreMutationGuard.Evaluate(p, "TEST_ONLY", plan, new List<string> { "GDAI__ronin__idle__front__f000" }, out _, out var code));
            StringAssert.StartsWith("STOP_STAGE_1A_SCOPE_BREACH", code);
        }

        // ── audit fix #1: a same-profile SUBSET package is refused PRE-write (no post-reslice prune) ──
        [Test]
        public void SubsetPackage_RefusedPreWrite_PriorAssetsByteIdentical()
        {
            WriteBaselineV1Manifest();
            Assert.IsTrue(RunRonin().ok, "first (full) materialization must pass");

            string sheetPath = "Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png";
            string clipPath = "Assets/GDAI_Project/Generated/Animations/Clips/GDAI__ronin__slash__back.anim";
            var fidsBefore = FileIds(sheetPath);
            byte[] clipBytesBefore = File.ReadAllBytes(Path.Combine(ProjectRoot(), clipPath));
            Assert.AreEqual(24, fidsBefore.Count);

            // build a valid SUBSET: drop the last clip (slash__back, row 5) + its 4 cells; recompute hash.
            var pkg = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            var clips = (JArray)pkg["clips"];
            string drop = (string)clips[clips.Count - 1]["clip_id"];
            clips.RemoveAt(clips.Count - 1);
            var cells = (JArray)pkg["cells"];
            for (int i = cells.Count - 1; i >= 0; i--) if ((string)cells[i]["clip_id"] == drop) cells.RemoveAt(i);
            pkg["package_id"] = "TESTONLY-norm-ronin-4x6-v1-subset";
            pkg["package_content_sha256"] = "";
            pkg["package_content_sha256"] = GdaiAnimJson.PackageContentSha256(pkg);

            var r = GdaiAnimationMaterializer.Run(pkg.ToString(), FixtureAbs(RoninPng), "TEST_ONLY");
            Assert.IsFalse(r.ok, "a subset (removal) package must be refused");
            StringAssert.StartsWith("STOP_STAGE_1A_SCOPE_BREACH", r.error, "refused by the removal wall, not a post-write prune");

            // prior assets byte-identical: NO sub-sprite was pruned, NO clip rewritten
            CollectionAssert.AreEqual(fidsBefore, FileIds(sheetPath), "all 24 sub-sprite fileIDs intact (no prune)");
            CollectionAssert.AreEqual(clipBytesBefore, File.ReadAllBytes(Path.Combine(ProjectRoot(), clipPath)), "dropped clip's asset untouched");
            Assert.AreEqual(24, AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath).OfType<Sprite>().Count());
        }

        // ── audit fix #3: declared grid must tile the real sheet (shipping path, not just tests) ──
        [Test]
        public void GridMismatch_RefusedBeforeSlicing()
        {
            WriteBaselineV1Manifest();
            var pkg = GdaiAnimJson.ParseObject(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            // same golden sheet (hash unchanged) but declare a grid that does NOT tile 256x384
            pkg["sheet"]["columns"] = 5; // 5*64=320 != 256
            pkg["package_content_sha256"] = "";
            pkg["package_content_sha256"] = GdaiAnimJson.PackageContentSha256(pkg);
            var r = GdaiAnimationMaterializer.Run(pkg.ToString(), FixtureAbs(RoninPng), "TEST_ONLY");
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GATE_SHEET_GRID_MISMATCH", r.error);
            Assert.IsFalse(Directory.Exists(Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Animations")), "no writes on grid refusal");
        }

        // ── audit fix #4: manifest section with owned records but null pin is refused ──
        [Test]
        public void ManifestSectionInconsistent_Refused()
        {
            WriteBaselineV1Manifest();
            Assert.IsTrue(RunRonin().ok);
            // corrupt the manifest: keep owned records, null the materialization pin
            string mAbs = Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Manifests/GDAIPlayableAssets.json");
            var m = JObject.Parse(File.ReadAllText(mAbs));
            m["animation_assets"]["materialization"] = null;
            File.WriteAllText(mAbs, m.ToString());
            AssetDatabase.Refresh();
            var r = RunRonin();
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GUARD_MANIFEST_SECTION_INCONSISTENT", r.error);
        }

        // ── audit fix #5: approval hands out a read-only snapshot, not the mutable backing set ──
        [Test]
        public void Approval_ApprovedPaths_IsNotMutableBackingSet()
        {
            WriteBaselineV1Manifest();
            var p = GdaiAnimationPackage.Parse(LoadPackageJson("TESTONLY-norm-ronin-4x6-v1.json"));
            Assert.IsTrue(GdaiPreMutationGuard.Evaluate(p, "TEST_ONLY", GdaiPreMutationGuard.BuildFixedSetPlan(p), new List<string>(), out var approval, out _));
            Assert.IsFalse(approval.ApprovedPaths is HashSet<string>, "must not expose a mutable HashSet");
            var ex = Assert.Throws<GdaiAnimGateException>(() => approval.AssertInPlan("Assets/Scripts/Evil.anim"));
            StringAssert.StartsWith("STOP_WRITE_NOT_IN_APPROVED_PLAN", ex.Code);
        }

        // ── the vertical slice: two differently-shaped packages through ONE importer ──
        [Test]
        public void TwoShapes_OneImporter_MaterializeBoth_ReceiptPASS()
        {
            WriteBaselineV1Manifest();
            var r1 = RunRonin();
            Assert.IsTrue(r1.ok, "ronin: " + r1.error + " · " + string.Join("; ", r1.verifyFailures));
            Assert.AreEqual("PASS", r1.receiptStatus);

            // sprite_name VERBATIM in the AssetDatabase
            var names = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png")
                .OfType<Sprite>().Select(s => s.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.AreEqual(24, names.Count);
            Assert.Contains("GDAI__ronin__idle__front__f000", names);
            Assert.Contains("GDAI__ronin__slash__back__f003", names);

            // manifest v2: namespace split + file_id STRING + dedicated label
            string manifestAbs = Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Manifests/GDAIPlayableAssets.json");
            var raw = JObject.Parse(File.ReadAllText(manifestAbs));
            Assert.AreEqual("gdai.unity.playable_assets.v2", (string)raw["schema_version"]);
            Assert.AreEqual("unity.pointer_action_demo.v1", (string)raw["profile_id"], "playable namespace untouched");
            Assert.AreEqual("JASON_RONIN_4X6_EXPERIMENT_V1", (string)raw["animation_assets"]["materialization"]["animation_profile_id"]);
            Assert.IsNull(raw["animation_assets"]["materialization"]["profile_id"], "no materialization.profile_id variant (R2)");
            Assert.AreEqual(JTokenType.String, raw["animation_assets"]["sprites"][0]["file_id"].Type, "file_id serialized as STRING (R1)");
            Assert.AreEqual("GDAI_Owned_Animation", (string)raw["animation_assets"]["sprites"][0]["label"]);
            // v1 fields preserved verbatim
            Assert.AreEqual("TESTONLY-project-0f", (string)raw["project_id"]);
            Assert.AreEqual(Snapshot, (string)raw["snapshot_id"]);
            Assert.AreEqual(4, (int)raw["contract_revision"]);

            // dedicated label discovery finds ONLY recorded owned assets (sheet+6 clips+controller)
            var labelled = AssetDatabase.FindAssets("l:GDAI_Owned_Animation", new[] { "Assets/GDAI_Project/Generated/Animations" });
            Assert.AreEqual(8, labelled.Length, "sheet + 6 clips + controller carry the dedicated label");

            // second shape through the SAME importer (no per-shape branch): skeleton 4x5@48.
            // Each Stage-1A materialization binds ONE package per manifest (0E-03 single pin);
            // a different entity/profile in the same state is correctly pin-refused, so the
            // two-fixture slice proves shape-agnosticism on a CLEAN state per shape.
            Clean();
            WriteBaselineV1Manifest();
            var r2 = RunSkeleton();
            Assert.IsTrue(r2.ok, "skeleton: " + r2.error + " · " + string.Join("; ", r2.verifyFailures));
            Assert.AreEqual("PASS", r2.receiptStatus);
            var skelNames = AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__skeleton.png")
                .OfType<Sprite>().Select(s => s.name).ToList();
            Assert.AreEqual(20, skelNames.Count);
            Assert.Contains("GDAI__skeleton__death__f000", skelNames);

            // receipt non-vacuity: rows exist and each row is a real, failable check
            var receipt = GdaiAnimationReceipt.Load();
            Assert.IsNotNull(receipt);
            Assert.Greater(receipt.checks.Count, 8);
            Assert.AreEqual(0, receipt.manual_assembly_steps);
            Assert.IsTrue(receipt.checks.Any(c => c.key == "verify_animation_assets" && c.pass));
            Assert.IsTrue(receipt.checks.Any(c => c.key == "hit_event_metadata_readback_only"), "events are METADATA-ONLY readback (no runtime claim)");
        }

        // ── identical second sync: GUID/fileID stability, clip-ref survival, zero duplicates ──
        [Test]
        public void SecondSync_Identical_GuidFileIdStable_NoShrink_NoDuplicates()
        {
            WriteBaselineV1Manifest();
            Assert.IsTrue(RunRonin().ok, "first sync must pass");

            string sheetPath = "Assets/GDAI_Project/Generated/Animations/Sprites/GDAI__ronin.png";
            string sheetGuid1 = AssetDatabase.AssetPathToGUID(sheetPath);
            var fids1 = FileIds(sheetPath);
            var clipGuids1 = ClipGuids();
            string ctrlGuid1 = AssetDatabase.AssetPathToGUID("Assets/GDAI_Project/Generated/Animations/Controllers/GDAI__ronin.controller");

            var r2 = RunRonin(); // byte-identical second sync
            Assert.IsTrue(r2.ok, "second sync: " + r2.error + " · " + string.Join("; ", r2.verifyFailures));
            Assert.AreEqual("PASS", r2.receiptStatus);
            Assert.AreEqual(24, r2.survivors, "all sprite_names survive");
            Assert.AreEqual(0, r2.additions);
            Assert.AreEqual(0, r2.section.reslice_diff.removals.Count, "plan.removals == empty (Stage 1A wall)");

            Assert.AreEqual(sheetGuid1, AssetDatabase.AssetPathToGUID(sheetPath), "sheet GUID stable");
            CollectionAssert.AreEqual(fids1, FileIds(sheetPath), "all 24 sub-sprite fileIDs stable");
            CollectionAssert.AreEqual(clipGuids1, ClipGuids(), "clip GUIDs stable");
            Assert.AreEqual(ctrlGuid1, AssetDatabase.AssetPathToGUID("Assets/GDAI_Project/Generated/Animations/Controllers/GDAI__ronin.controller"));

            // zero duplicates: still exactly 24 sprites / 6 clips / 6 controller states
            Assert.AreEqual(24, AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath).OfType<Sprite>().Count());
            Assert.AreEqual(6, Directory.GetFiles(Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Animations/Clips"), "*.anim").Length);
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/GDAI_Project/Generated/Animations/Controllers/GDAI__ronin.controller");
            Assert.AreEqual(6, ctrl.layers[0].stateMachine.states.Length);

            // clip object references survive (every keyframe resolves to a live sprite)
            var failures = new List<string>();
            GdaiVerifyAnimationAssets.Verify(GdaiPlayableOwnershipManifest.Load(), failures);
            Assert.IsEmpty(failures, string.Join("; ", failures));
        }

        // ── human-edit fail-closed: Verify + next-run guard both refuse ──
        [Test]
        public void HumanEdit_ClipTampered_VerifyAndGuardFailClosed()
        {
            WriteBaselineV1Manifest();
            Assert.IsTrue(RunRonin().ok);
            string clipAbs = Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Animations/Clips/GDAI__ronin__idle__front.anim");
            File.AppendAllText(clipAbs, "\n# human edit\n");
            AssetDatabase.Refresh();

            var failures = new List<string>();
            GdaiVerifyAnimationAssets.Verify(GdaiPlayableOwnershipManifest.Load(), failures);
            Assert.IsTrue(failures.Any(f => f.StartsWith("VERIFY_ANIM_HUMAN_EDITED:clip")), string.Join("; ", failures));

            var r = RunRonin();
            Assert.IsFalse(r.ok);
            StringAssert.StartsWith("GUARD_FINGERPRINT_DRIFT", r.error);
        }

        // ── Stage 1A safety: the animation code path contains no delete API at all ──
        [Test]
        public void NoDeleteAsset_ReachableInAnimationSources()
        {
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/ai.gamedevs.plugin/package.json");
            string dir = pi != null
                ? Path.Combine(pi.resolvedPath, "Assets/GDAI/Editor/LayerC/Animation")
                : Path.GetFullPath(Path.Combine(ProjectRoot(), "Assets/GDAI/Editor/LayerC/Animation"));
            var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(sources.Length, 4, "locator non-vacuous");
            foreach (var f in sources)
            {
                string src = File.ReadAllText(f);
                StringAssert.DoesNotContain("DeleteAsset", src, f);
                StringAssert.DoesNotContain("MoveAssetToTrash", src, f);
                StringAssert.DoesNotContain("File.Delete(", src, f);
            }
        }

        static List<long> FileIds(string sheetPath)
        {
            var list = new List<long>();
            foreach (var s in AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath).OfType<Sprite>()
                .OrderBy(s => s.name, StringComparer.Ordinal))
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out _, out long fid)) list.Add(fid);
            return list;
        }

        static List<string> ClipGuids() =>
            Directory.GetFiles(Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Animations/Clips"), "*.anim")
                .Select(f => "Assets" + f.Replace('\\', '/').Substring(Path.Combine(ProjectRoot(), "Assets").Replace('\\', '/').Length))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.AssetPathToGUID).ToList();
    }
}
