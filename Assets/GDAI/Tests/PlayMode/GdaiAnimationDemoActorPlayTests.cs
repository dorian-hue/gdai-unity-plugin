// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · Play-mode proof — DEMO ACTOR ONLY (0E-04).
//
// UTF 1.x pattern: editor-platform [UnityTest]s that `yield return new EnterPlayMode()`,
// observe REAL play-mode behavior, then ExitPlayMode. Setup (materialization via the
// guarded importer) happens before entering play; nothing created before the boundary is
// needed after it except re-derivable consts. Asserts: sprite visible · default clip
// plays · frames advance at nominal_fps · after the byte-identical second sync every
// reference still resolves. NO runtime hit-window assertion exists here
// (STOP_STAGE_1A_RUNTIME_WINDOW_CLAIMED — event semantics are metadata-only in Stage 1A;
// runtime open/close/cancel behavior is Stage 2). No canonical Player/Enemy is touched.
// =====================================================================================
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerC.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GDAI.Bridge.PlayMode.Tests
{
    public class GdaiAnimationDemoActorPlayTests
    {
        const string FixtureRel = "Assets/GDAI/Tests/Editor/Fixtures/Animation";
        const string ControllerPath = "Assets/GDAI_Project/Generated/Animations/Controllers/GDAI__ronin.controller";
        const string Snapshot = "TESTONLY-snap-0f";
        static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        static string FixtureAbs(string file)
        {
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/ai.gamedevs.plugin/package.json");
            string root = pi != null ? Path.Combine(pi.resolvedPath, FixtureRel) : Path.GetFullPath(Path.Combine(ProjectRoot(), FixtureRel));
            return Path.Combine(root, file);
        }

        [OneTimeSetUp]
        public void FastDeterministicPlayModeEntry()
        {
            // host-project test setting only (never a product/package write): skip the domain reload on
            // EnterPlayMode so the UnityTest coroutine resumes deterministically.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        static void EnsureMaterialized(int runs)
        {
            string mAbs = Path.Combine(ProjectRoot(), "Assets/GDAI_Project/Generated/Manifests/GDAIPlayableAssets.json");
            if (!File.Exists(mAbs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mAbs));
                File.WriteAllText(mAbs,
                    "{\n" +
                    "  \"schema_version\": \"gdai.unity.playable_assets.v1\",\n" +
                    "  \"project_id\": \"TESTONLY-project-0f\",\n" +
                    "  \"profile_id\": \"unity.pointer_action_demo.v1\",\n" +
                    "  \"snapshot_id\": \"" + Snapshot + "\",\n" +
                    "  \"contract_revision\": 4,\n" +
                    "  \"contract_sha256\": \"" + new string('0', 64) + "\",\n" +
                    "  \"generated_at\": \"2026-07-14T00:00:00.0000000Z\",\n" +
                    "  \"input_asset\": {\"kind\":\"input\",\"path\":\"X\",\"guid\":\"g\"},\n" +
                    "  \"enemy_prefab\": {\"kind\":\"enemy_prefab\",\"path\":\"X\",\"guid\":\"g\"},\n" +
                    "  \"canonical_scene\": {\"kind\":\"canonical_scene\",\"path\":\"X\",\"guid\":\"g\"},\n" +
                    "  \"assets\": [],\n" +
                    "  \"owned_scene_objects\": [{\"name\":\"T\",\"role\":\"t\",\"profile_id\":\"unity.pointer_action_demo.v1\",\"snapshot_id\":\"" + Snapshot + "\"}]\n" +
                    "}\n");
                AssetDatabase.Refresh();
            }
            string packageJson = File.ReadAllText(FixtureAbs("TESTONLY-norm-ronin-4x6-v1.json"));
            for (int i = 0; i < runs; i++)
            {
                var r = GdaiAnimationMaterializer.Run(packageJson, FixtureAbs("TESTONLY-ronin-4x6-64.png"), "TEST_ONLY");
                Assert.IsTrue(r.ok, "materialize (run " + (i + 1) + "): " + r.error + " · " + string.Join("; ", r.verifyFailures));
            }
        }

        static GameObject BuildDemoActor()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            Assert.IsNotNull(ctrl, "owned controller present");
            var go = new GameObject("GDAI_TestOnly_DemoActor");
            go.AddComponent<SpriteRenderer>();
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            return go;
        }

        [UnityTest]
        public IEnumerator DemoActor_SpriteVisible_DefaultClipPlays_FramesAdvanceAtNominalFps()
        {
            EnsureMaterialized(runs: 1);
            yield return new EnterPlayMode();

            var actor = BuildDemoActor();
            var sr = actor.GetComponent<SpriteRenderer>();
            var animator = actor.GetComponent<Animator>();

            yield return null; // first animated frame applied
            Assert.IsTrue(sr.enabled && actor.activeInHierarchy, "renderer active");
            Assert.IsNotNull(sr.sprite, "sprite visible (assigned by the playing clip)");
            StringAssert.StartsWith("GDAI__ronin__", sr.sprite.name);

            var state = animator.GetCurrentAnimatorStateInfo(0);
            Assert.IsTrue(state.length > 0f, "default state playing");
            var clipInfo = animator.GetCurrentAnimatorClipInfo(0);
            Assert.IsTrue(clipInfo.Length > 0 && clipInfo[0].clip != null, "default clip bound");
            Assert.AreEqual(12f, clipInfo[0].clip.frameRate, 0.001f, "nominal_fps applied to the clip");

            // frames advance at ~12fps: over ~0.6s expect ≥3 distinct owned frame sprites
            var seen = new List<string>();
            float t = 0f;
            while (t < 0.6f)
            {
                if (sr.sprite != null && (seen.Count == 0 || seen[seen.Count - 1] != sr.sprite.name)) seen.Add(sr.sprite.name);
                yield return null;
                t += Time.deltaTime;
            }
            Assert.GreaterOrEqual(seen.Distinct().Count(), 3, "frames advance (saw: " + string.Join(",", seen) + ")");
            Assert.IsTrue(seen.All(n => n.StartsWith("GDAI__ronin__", System.StringComparison.Ordinal)), "all frames are owned sprites");

            Object.Destroy(actor);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator DemoActor_AfterIdenticalSecondSync_ReferencesStillResolve()
        {
            EnsureMaterialized(runs: 2); // byte-identical second sync BEFORE entering play

            // every clip keyframe still resolves to a live sprite (no dangling refs) — edit-state fact
            var failures = new List<string>();
            GdaiVerifyAnimationAssets.Verify(GDAI.Bridge.Editor.LayerC.GdaiPlayableOwnershipManifest.Load(), failures);
            Assert.IsEmpty(failures, string.Join("; ", failures));

            yield return new EnterPlayMode();

            var actor = BuildDemoActor();
            var sr = actor.GetComponent<SpriteRenderer>();
            yield return null;
            Assert.IsNotNull(sr.sprite, "sprite still resolves after second sync");
            StringAssert.StartsWith("GDAI__ronin__", sr.sprite.name);

            float t = 0f; string first = sr.sprite.name; bool advanced = false;
            while (t < 0.6f && !advanced)
            {
                yield return null;
                t += Time.deltaTime;
                advanced = sr.sprite != null && sr.sprite.name != first;
            }
            Assert.IsTrue(advanced, "animation still advances after second sync");

            Object.Destroy(actor);
            yield return new ExitPlayMode();
        }
    }
}
