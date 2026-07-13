// AUTO-0Q-P2 · §5.4 tests · Contract-driven binding applier.
// Proves all 7 scene_bindings + the LayerMask value_binding land on the REAL generated
// components (by reflection field type, verified to stick), idempotently, fail-closed.
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Bridge.Editor.LayerC;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiPlayableBindingApplierTests
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private GdaiPlayableContract _contract;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        private Component Comp(string owned, string typeName) =>
            GdaiSceneObjectComposer.FindOwned(owned).GetComponent(GdaiSceneObjectComposer.ResolveComponentType(typeName));

        private SerializedObject SoOf(string owned, string typeName) => new SerializedObject(Comp(owned, typeName));

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "fixture must parse");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            var compose = GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
            Assert.IsTrue(compose.Ok, compose.Summary);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            foreach (var s in EditorBuildSettings.scenes.Where(s => s.path == ScenePath).ToArray())
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(x => x.path != ScenePath).ToArray();
        }

        [Test]
        public void Apply_BindsAllSevenSceneRefs_AndValueBinding()
        {
            var r = GdaiPlayableBindingApplier.Apply(_contract);
            Assert.IsTrue(r.Ok, r.Summary);
            Assert.AreEqual(7, r.SceneBound, "all 7 scene refs bound");
            Assert.AreEqual(_contract.scene_bindings.Count, r.SceneTotal);
            Assert.AreEqual(1, r.ValueBound, "the enemyLayer value binding bound");

            var player = GdaiSceneObjectComposer.FindOwned("Player");
            var camera = GdaiSceneObjectComposer.FindOwned("Main Camera");

            // GameIntegrationController: 5 refs
            var gic = SoOf("GameIntegrationController", "GameIntegrationController");
            Assert.AreEqual(Comp("InputManager", "InputManager"), gic.FindProperty("inputManager").objectReferenceValue, "GIC.inputManager");
            Assert.AreEqual(Comp("Player", "CharacterStateMachine"), gic.FindProperty("characterStateMachine").objectReferenceValue, "GIC.characterStateMachine");
            Assert.AreEqual(Comp("Player", "CombatLocatorSystem"), gic.FindProperty("combatLocatorSystem").objectReferenceValue, "GIC.combatLocatorSystem");
            Assert.AreEqual(Comp("EnemyManager", "EnemyDirector"), gic.FindProperty("enemyDirector").objectReferenceValue, "GIC.enemyDirector");
            Assert.AreEqual(player.transform, gic.FindProperty("player").objectReferenceValue, "GIC.player is the Player Transform");

            // InputManager: 2 scene refs (Camera component + Player transform)
            var im = SoOf("InputManager", "InputManager");
            Assert.AreEqual(camera.GetComponent<Camera>(), im.FindProperty("_mainCamera").objectReferenceValue, "InputManager._mainCamera is the Camera");
            Assert.AreEqual(player.transform, im.FindProperty("_playerReferenceTransform").objectReferenceValue, "InputManager._playerReferenceTransform");

            // value binding: CombatLocatorSystem.enemyLayer includes Default (layer 0 → bit 0)
            var cls = SoOf("Player", "CombatLocatorSystem");
            int mask = cls.FindProperty("enemyLayer").intValue;
            Assert.AreNotEqual(0, mask, "enemyLayer mask is non-empty");
            Assert.AreNotEqual(0, mask & (1 << LayerMask.NameToLayer("Default")), "enemyLayer includes the enemy's Default layer");
        }

        [Test]
        public void Apply_Idempotent_SecondApplyStillFullyBound()
        {
            var r1 = GdaiPlayableBindingApplier.Apply(_contract);
            Assert.IsTrue(r1.Ok, r1.Summary);
            var r2 = GdaiPlayableBindingApplier.Apply(_contract);
            Assert.IsTrue(r2.Ok, r2.Summary);
            Assert.AreEqual(7, r2.SceneBound);
            Assert.AreEqual(1, r2.ValueBound);
        }

        [Test]
        public void Apply_FailsClosed_WhenSourceHostMissing()
        {
            // remove the owned GameIntegrationController → its 5 source bindings must fail, not silently pass.
            Object.DestroyImmediate(GdaiSceneObjectComposer.FindOwned("GameIntegrationController"));
            var r = GdaiPlayableBindingApplier.Apply(_contract);
            Assert.IsFalse(r.Ok, "missing source host must fail closed");
            Assert.Less(r.SceneBound, 7, "the GIC-hosted refs are not counted as bound");
            Assert.IsTrue(r.Errors.Any(e => e.Contains("GameIntegrationController")), "explicit unresolved-host error");
        }
    }
}
