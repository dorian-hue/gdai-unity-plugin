// AUTO-0Q-P2 · §5.4b/§5.5 tests · AudioListener policy, ownership manifest, hard receipt, full CTA.
using System;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using GDAI.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.Tests
{
    // ---------- AudioListener discipline ----------
    public class GdaiAudioListenerEnsurerTests
    {
        [SetUp] public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        private static GameObject OwnedCamera()
        {
            var go = new GameObject("Main Camera");
            var m = go.AddComponent<GdaiGeneratedPlayableMarker>();
            m.ownedRole = "camera"; m.profileId = "p"; m.snapshotId = "s";
            go.AddComponent<Camera>();
            return go;
        }

        [Test]
        public void ZeroActive_AddsToOwnedCamera()
        {
            var cam = OwnedCamera();
            var r = GdaiAudioListenerEnsurer.Ensure();
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(GdaiAudioListenerEnsurer.Outcome.AddedToOwnedCamera, r.Outcome);
            Assert.AreEqual(1, r.ActiveCount);
            Assert.IsNotNull(cam.GetComponent<AudioListener>(), "listener added to the owned camera");
        }

        [Test]
        public void OneActive_Preserved_NoSecondAdded()
        {
            OwnedCamera();
            var human = new GameObject("HumanEars"); human.AddComponent<AudioListener>();
            var r = GdaiAudioListenerEnsurer.Ensure();
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(GdaiAudioListenerEnsurer.Outcome.PreservedExistingOne, r.Outcome);
            Assert.AreEqual(1, GdaiAudioListenerEnsurer.ActiveCount(), "still exactly one — no second added");
        }

        [Test]
        public void MoreThanOneActive_FailsClosed_NeverDeletes()
        {
            var a = new GameObject("EarsA"); a.AddComponent<AudioListener>();
            var b = new GameObject("EarsB"); b.AddComponent<AudioListener>();
            var r = GdaiAudioListenerEnsurer.Ensure();
            Assert.IsFalse(r.Ok, "two active listeners must fail closed");
            Assert.AreEqual(GdaiAudioListenerEnsurer.Outcome.FailedTooMany, r.Outcome);
            Assert.IsNotNull(a.GetComponent<AudioListener>(), "user listener A never deleted");
            Assert.IsNotNull(b.GetComponent<AudioListener>(), "user listener B never deleted");
        }

        [Test]
        public void InactiveListener_NotCounted_NotDeleted()
        {
            var cam = OwnedCamera();
            var inactive = new GameObject("SleepingEars"); inactive.AddComponent<AudioListener>(); inactive.SetActive(false);
            var r = GdaiAudioListenerEnsurer.Ensure();
            Assert.IsTrue(r.Ok, "inactive listener does not count → add to owned camera");
            Assert.AreEqual(1, GdaiAudioListenerEnsurer.ActiveCount());
            Assert.IsNotNull(inactive.GetComponent<AudioListener>(), "inactive human listener untouched");
        }

        [Test]
        public void NoOwnedCamera_FailsClosed()
        {
            new GameObject("Main Camera").AddComponent<Camera>(); // unmarked human camera
            var r = GdaiAudioListenerEnsurer.Ensure();
            Assert.IsFalse(r.Ok, "no owned camera → fail closed, never adopt the human Main Camera");
            Assert.AreEqual(GdaiAudioListenerEnsurer.Outcome.FailedNoOwnedCamera, r.Outcome);
        }
    }

    // ---------- Full CTA + hard receipt + ownership manifest ----------
    public class GdaiPlayableCtaReceiptTests
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string PID = "18bedbf4-3993-422b-97ce-e5eb910bb55c";
        private const string SNAP = "2d874a40-f10d-4d90-a3c4-5c7ffdb7cdee";
        private const string SHA = "12af83d4bc487687c8207b48a50816a25571c5b952c7eedb3def5015f508d043";
        private static readonly DateTime T = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc);
        private GdaiPlayableContract _contract;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        private static void CleanArtifacts()
        {
            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            if (AssetDatabase.IsValidFolder("Assets/GDAI_Project")) AssetDatabase.DeleteAsset("Assets/GDAI_Project");
            var opsDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "GDAI", "operations");
            if (Directory.Exists(opsDir)) { try { Directory.Delete(opsDir, true); } catch { } }
            AssetDatabase.Refresh();
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "fixture must parse");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CleanArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            CleanArtifacts();
            foreach (var s in EditorBuildSettings.scenes.Where(s => s.path == ScenePath).ToArray())
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(x => x.path != ScenePath).ToArray();
        }

        [Test]
        public void Cta_FullRun_ProducesPassReceipt_AndCompletesOperation()
        {
            var res = GdaiPlayableComposerCta.Run(_contract, PID, SNAP, SHA, ScenePath, T);
            Assert.IsNull(res.Error, "CTA error: " + res.Error + "\nlog:\n" + string.Join("\n", res.Log));
            Assert.IsNotNull(res.Receipt, "receipt produced");
            Assert.AreEqual("PASS", res.Receipt.status,
                "receipt not PASS. failures:\n  " + string.Join("\n  ", res.Receipt.failures));
            Assert.IsTrue(res.Completed, "operation Completed only on a PASS receipt");
            Assert.AreEqual(0, res.Receipt.manual_assembly_steps, "manual_assembly_steps must be 0");

            var checks = res.Receipt.checks.ToDictionary(x => x.key, x => x);
            Assert.AreEqual("5", checks["objects_owned"].actual, "5/5 owned objects");
            Assert.AreEqual("7", checks["scene_refs"].actual, "7/7 scene refs");
            Assert.AreEqual("3", checks["input_refs"].actual, "3/3 input refs");
            Assert.AreEqual("1", checks["active_audio_listeners"].actual, "exactly one active AudioListener");
            Assert.AreEqual("1", checks["value_bindings"].actual, "value binding bound");
            Assert.IsTrue(checks["enemy_prefab_assigned"].pass, "enemy prefab assigned");
            Assert.IsTrue(checks["enemy_collider_nonzero"].pass, "enemy collider non-zero");
            Assert.IsTrue(checks["camera_ortho_size"].pass, "camera solved size: " + checks["camera_ortho_size"].actual);
            Assert.IsTrue(checks["camera_position_z"].pass, "camera z");
            Assert.IsTrue(checks["ownership_manifest"].pass, "manifest verified");

            // artifacts exist on disk
            Assert.IsTrue(File.Exists(GdaiPlayableReceipt.ReceiptPath), "receipt written");
            Assert.IsTrue(File.Exists(GdaiPlayableOwnershipManifest.ManifestPath), "manifest written");
        }

        [Test]
        public void Receipt_NotPass_WhenAnOwnedObjectIsRemovedBeforeReadback()
        {
            // full compose first
            var res = GdaiPlayableComposerCta.Run(_contract, PID, SNAP, SHA, ScenePath, T);
            Assert.AreEqual("PASS", res.Receipt.status, string.Join("; ", res.Receipt.failures));

            // tamper: delete the EnemyManager from the SAVED scene, then rebuild the receipt by readback
            UnityEngine.Object.DestroyImmediate(GdaiSceneObjectComposer.FindOwned("EnemyManager"));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            var receipt2 = GdaiPlayableReceiptWriter.Build(_contract, PID, SNAP, SHA, ScenePath, T);
            Assert.AreNotEqual("PASS", receipt2.status, "a missing owned object must never round up to PASS");
            Assert.IsTrue(receipt2.failures.Any(), "failures recorded");
        }

        [Test]
        public void OwnershipManifest_Verify_FailsWhenMarkerMissing()
        {
            var res = GdaiPlayableComposerCta.Run(_contract, PID, SNAP, SHA, ScenePath, T);
            Assert.AreEqual("PASS", res.Receipt.status, string.Join("; ", res.Receipt.failures));
            Assert.IsTrue(GdaiPlayableOwnershipManifest.Verify(out _), "manifest agrees right after a PASS run");

            // remove a recorded owned object → manifest no longer agrees with actual state
            UnityEngine.Object.DestroyImmediate(GdaiSceneObjectComposer.FindOwned("Player"));
            Assert.IsFalse(GdaiPlayableOwnershipManifest.Verify(out string err), "manifest must fail closed on a missing marker");
            StringAssert.Contains("Player", err);
        }
    }
}
