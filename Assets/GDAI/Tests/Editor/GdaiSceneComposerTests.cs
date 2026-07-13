// AUTO-0Q-P2 · P2-C tests · Canonical scene lifecycle + 5-object composer.
// Drives the composer with the REAL rev3 contract fixture; verifies component
// types by the same string resolution the composer uses (the generated types
// live in the host's default assembly, not the test asmdef).
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using GDAI.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiSceneComposerTests
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private GdaiPlayableContract _contract;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev3.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "fixture must parse");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(ScenePath)) { AssetDatabase.DeleteAsset(ScenePath); }
            foreach (var s in EditorBuildSettings.scenes.Where(s => s.path == ScenePath).ToArray())
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(x => x.path != ScenePath).ToArray();
        }

        [Test]
        public void CanonicalScene_CreatesSavesAndRegisters()
        {
            var r = GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.IsTrue(r.Created);
            Assert.IsTrue(File.Exists(ScenePath), "scene saved to exact path (no Save As)");
            Assert.IsTrue(GdaiCanonicalScene.IsInBuildSettings(ScenePath));
            Assert.AreEqual(ScenePath, SceneManager.GetActiveScene().path);
        }

        [Test]
        public void CanonicalScene_ReusesExistingSamePath()
        {
            var first = GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            var guid1 = AssetDatabase.AssetPathToGUID(ScenePath);
            var second = GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            Assert.IsTrue(second.Ok);
            Assert.IsFalse(second.Created, "second call reuses, does not recreate");
            Assert.AreEqual(guid1, AssetDatabase.AssetPathToGUID(ScenePath), "scene GUID stable");
        }

        [Test]
        public void CanonicalScene_RejectsNonScenePath()
        {
            Assert.IsFalse(GdaiCanonicalScene.EnsureSavedAndInBuild("Assets/GDAI_Project/x.prefab").Ok);
            Assert.IsFalse(GdaiCanonicalScene.EnsureSavedAndInBuild("/tmp/Main.unity").Ok);
        }

        [Test]
        public void Composer_CreatesFiveMarkedObjectsWithComponents()
        {
            GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            var r = GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r.Ok, r.Summary);
            Assert.AreEqual(5, r.Created.Count);

            foreach (var spec in _contract.scene_objects)
            {
                var go = GdaiSceneObjectComposer.FindOwned(spec.name);
                Assert.IsNotNull(go, "object " + spec.name + " created");
                Assert.IsNotNull(go.GetComponent<GdaiGeneratedPlayableMarker>(), spec.name + " is GDAI-marked");
                foreach (var comp in spec.components)
                {
                    var t = GdaiSceneObjectComposer.ResolveComponentType(comp);
                    Assert.IsNotNull(t, "resolve " + comp);
                    Assert.IsNotNull(go.GetComponent(t), spec.name + " has " + comp);
                }
            }
            // player co-location + tag
            var player = GdaiSceneObjectComposer.FindOwned("Player");
            Assert.AreEqual("Player", player.tag);
            Assert.IsNotNull(player.GetComponent(GdaiSceneObjectComposer.ResolveComponentType("CharacterStateMachine")));
            Assert.IsNotNull(player.GetComponent(GdaiSceneObjectComposer.ResolveComponentType("CombatLocatorSystem")));
            Assert.IsNotNull(player.GetComponent<SpriteRenderer>());
            Assert.IsNotNull(player.GetComponent<Rigidbody2D>());
        }

        [Test]
        public void Composer_Idempotent_NoDuplicatesOnSecondRun()
        {
            GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
            var r2 = GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r2.Ok, r2.Summary);
            Assert.AreEqual(0, r2.Created.Count, "second compose creates nothing new");
            var markers = Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsSortMode.None);
            Assert.AreEqual(5, markers.Length, "exactly five owned objects, no duplicates");
        }

        [Test]
        public void ResolveType_FailsClosed_NoMatchAmbiguousAbstract()
        {
            // 0 match → null
            GdaiSceneObjectComposer.ResolveComponentType("NoSuchGeneratedComponent_xyz", out var o0);
            Assert.AreEqual(GdaiSceneObjectComposer.ResolveOutcome.NoMatch, o0);
            // >1 compatible runtime match → Ambiguous (never FirstOrDefault)
            GdaiSceneObjectComposer.ResolveComponentType("DupComponent", out var oAmb);
            Assert.AreEqual(GdaiSceneObjectComposer.ResolveOutcome.Ambiguous, oAmb);
            // abstract engine base Collider2D is not directly instantiable via the generated path;
            // a unique generated MonoBehaviour resolves.
            GdaiSceneObjectComposer.ResolveComponentType("EnemyDirector", out var oOk);
            Assert.AreEqual(GdaiSceneObjectComposer.ResolveOutcome.Resolved, oOk);
        }

        [Test]
        public void Composer_RefusesToAdoptUnmarkedHumanObject()
        {
            GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            // a human-made 'Player' with no marker must NOT be adopted
            var human = new GameObject("Player");
            try
            {
                var r = GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
                Assert.IsFalse(r.Ok, "must refuse to adopt an unmarked same-named object");
                Assert.IsTrue(r.Errors.Any(e => e.Contains("not GDAI-owned")));
            }
            finally { Object.DestroyImmediate(human); }
        }
    }
}
