// AUTO-0Q-P2 · P2-D3 tests · Enemy prefab builder.
// Proves the owned enemy prefab is created with SpriteRenderer + Rigidbody2D + a
// concrete Collider2D + ownership marker on the contract's Default layer, the
// collider is enabled + non-zero + fit to the sprite (hittable geometry), the GUID
// is stable across rebuilds, a same-path UNMARKED human prefab is never overwritten,
// and the built prefab binds into EnemyDirector.enemyPrefab.
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
    public class GdaiEnemyPrefabBuilderTests
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private GdaiPlayableContract _contract;
        private Sprite _sprite;
        private Texture2D _tex;
        private string PrefabPath => _contract.enemy_prefab.asset_path;

        private static string FixtureJson()
        {
            var g = AssetDatabase.FindAssets("PlayableContract.rev4.projectslash-2d874a40");
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(g[0]));
        }

        [SetUp]
        public void SetUp()
        {
            _contract = GdaiPlayableContract.Parse(FixtureJson()).Contract;
            Assert.IsNotNull(_contract, "fixture must parse");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // hermetic start: remove any prefab/folder left by a prior (possibly killed) run so the
            // owned-path ownership gate sees a genuinely clean path.
            if (File.Exists(PrefabPath)) AssetDatabase.DeleteAsset(PrefabPath);
            if (AssetDatabase.IsValidFolder("Assets/GDAI_Project")) AssetDatabase.DeleteAsset("Assets/GDAI_Project");
            AssetDatabase.Refresh();

            // synthetic sprite: 64px @ PPU 100 → 0.64 world units, centered pivot.
            _tex = new Texture2D(64, 64);
            _sprite = Sprite.Create(_tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            _sprite.name = "TestEnemySprite";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(PrefabPath) && File.Exists(PrefabPath)) AssetDatabase.DeleteAsset(PrefabPath);
            if (AssetDatabase.IsValidFolder("Assets/GDAI_Project")) AssetDatabase.DeleteAsset("Assets/GDAI_Project");
            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            if (_sprite != null) Object.DestroyImmediate(_sprite);
            if (_tex != null) Object.DestroyImmediate(_tex);
        }

        [Test]
        public void Build_CreatesOwnedPrefab_WithComponentsLayerAndFitCollider()
        {
            var r = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r.Ok, r.Summary);
            Assert.IsTrue(r.Created);
            Assert.IsTrue(File.Exists(PrefabPath), "prefab file written to the contract's owned path");

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(asset);
            Assert.AreEqual(_contract.enemy_prefab.object_name, asset.name);
            Assert.AreEqual(LayerMask.NameToLayer("Default"), asset.layer, "on the contract's Default layer");
            Assert.IsNotNull(asset.GetComponent<SpriteRenderer>(), "SpriteRenderer");
            Assert.IsNotNull(asset.GetComponent<Rigidbody2D>(), "Rigidbody2D");
            Assert.IsNotNull(asset.GetComponent<GdaiGeneratedPlayableMarker>(), "ownership marker");
            Assert.AreEqual("enemy", asset.GetComponent<GdaiGeneratedPlayableMarker>().ownedRole);

            var box = asset.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(box, "concrete BoxCollider2D");
            Assert.IsTrue(box.enabled, "collider enabled");
            Assert.Greater(box.size.x * box.size.y, 0f, "non-zero collider area");
            // fit to sprite (0.64 world units) → enabled + non-zero + overlaps the visible pixels
            Assert.That(box.size.x, Is.EqualTo(_sprite.bounds.size.x).Within(0.001f), "collider width == sprite width");
            Assert.That(box.size.y, Is.EqualTo(_sprite.bounds.size.y).Within(0.001f), "collider height == sprite height");
            Assert.That(box.offset.x, Is.EqualTo(_sprite.bounds.center.x).Within(0.001f), "collider centered on sprite");
        }

        [Test]
        public void Build_Idempotent_StableGuid_NoDuplicateAsset()
        {
            var r1 = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r1.Ok, r1.Summary);
            string guid1 = r1.PrefabGuid;

            var r2 = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r2.Ok, r2.Summary);
            Assert.IsFalse(r2.Created, "second build updates in place, does not recreate");
            Assert.AreEqual(guid1, r2.PrefabGuid, "GUID stable across rebuilds");

            // §5.3 "no duplicate components": the rebuild must not stack a second collider/renderer/etc.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(1, asset.GetComponents<BoxCollider2D>().Length, "exactly one BoxCollider2D after rebuild");
            Assert.AreEqual(1, asset.GetComponents<SpriteRenderer>().Length, "exactly one SpriteRenderer after rebuild");
            Assert.AreEqual(1, asset.GetComponents<Rigidbody2D>().Length, "exactly one Rigidbody2D after rebuild");
            Assert.AreEqual(1, asset.GetComponents<GdaiGeneratedPlayableMarker>().Length, "exactly one ownership marker after rebuild");
        }

        [Test]
        public void Build_RefusesToOverwriteUnmarkedHumanPrefab()
        {
            // a human prefab already sits at the same path, with NO GDAI marker.
            GdaiEnemyPrefabBuilderTestUtil.EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var human = new GameObject(_contract.enemy_prefab.object_name);
            human.AddComponent<SpriteRenderer>();
            PrefabUtility.SaveAsPrefabAsset(human, PrefabPath, out bool ok);
            Object.DestroyImmediate(human);
            Assert.IsTrue(ok, "precondition: human prefab saved");
            string humanGuid = AssetDatabase.AssetPathToGUID(PrefabPath);

            var r = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsFalse(r.Ok, "must refuse to overwrite an unmarked human prefab");
            Assert.IsTrue(r.Errors.Any(e => e.Contains("not GDAI-owned")), "explicit refuse error");

            var stillHuman = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(stillHuman, "human prefab preserved");
            Assert.IsNull(stillHuman.GetComponent<GdaiGeneratedPlayableMarker>(), "human prefab never stamped");
            Assert.AreEqual(humanGuid, AssetDatabase.AssetPathToGUID(PrefabPath), "human prefab GUID unchanged");
        }

        [Test]
        public void Build_FitsCollider_ToOffCenterSprite_OffsetNonZeroBothAxes()
        {
            // An OFF-CENTER pivot moves sprite.bounds.center away from (0,0); a fresh BoxCollider2D
            // defaults to offset (0,0), so this catches a dropped/mis-set offset that a centered
            // sprite would hide (proves "meaningfully overlaps the sprite body", not vacuously).
            var tex = new Texture2D(64, 64);
            var offCenter = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0f, 0f), 100f); // bottom-left pivot
            try
            {
                var r = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, offCenter, _contract.profile_id, "2d874a40");
                Assert.IsTrue(r.Ok, r.Summary);
                var box = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BoxCollider2D>();
                var center = offCenter.bounds.center;
                Assert.AreNotEqual(0f, center.x, "precondition: off-center sprite has non-zero center.x");
                Assert.AreNotEqual(0f, center.y, "precondition: off-center sprite has non-zero center.y");
                Assert.That(box.offset.x, Is.EqualTo(center.x).Within(0.001f), "collider offset.x tracks sprite center.x");
                Assert.That(box.offset.y, Is.EqualTo(center.y).Within(0.001f), "collider offset.y tracks sprite center.y");
            }
            finally { Object.DestroyImmediate(offCenter); Object.DestroyImmediate(tex); }
        }

        [Test]
        public void Build_AppliesNonDefaultLayer()
        {
            // Default == layer 0 == every new GameObject's starting layer, so asserting Default is
            // vacuous. Exercise the assignment path with a defined non-zero built-in layer (UI = 5).
            var spec = new GdaiPlayableContract.EnemyPrefabSpec
            {
                asset_path = _contract.enemy_prefab.asset_path,
                object_name = _contract.enemy_prefab.object_name,
                layer = "UI", // built-in Unity layer 5, always defined
                sprite_role = "enemy",
                sprite_entity_id = _contract.enemy_prefab.sprite_entity_id,
                required_runtime_components = _contract.enemy_prefab.required_runtime_components,
                director_bindings = _contract.enemy_prefab.director_bindings,
            };
            Assert.AreEqual(5, LayerMask.NameToLayer("UI"), "precondition: UI is layer 5");
            var r = GdaiEnemyPrefabBuilder.Build(spec, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsTrue(r.Ok, r.Summary);
            Assert.AreEqual(5, AssetDatabase.LoadAssetAtPath<GameObject>(spec.asset_path).layer,
                "the contract's layer is actually assigned (not left at default 0)");
        }

        [Test]
        public void BindToDirector_AssignsEnemyPrefabOnManager()
        {
            GdaiCanonicalScene.EnsureSavedAndInBuild(ScenePath);
            var compose = GdaiSceneObjectComposer.Compose(_contract, _contract.profile_id, "2d874a40");
            Assert.IsTrue(compose.Ok, compose.Summary);

            var build = GdaiEnemyPrefabBuilder.Build(_contract.enemy_prefab, _sprite, _contract.profile_id, "2d874a40");
            Assert.IsTrue(build.Ok, build.Summary);

            bool bound = GdaiEnemyPrefabBuilder.BindToDirector(PrefabPath, _contract.enemy_prefab, out string err);
            Assert.IsTrue(bound, err);

            var manager = GdaiSceneObjectComposer.FindOwned("EnemyManager");
            var director = manager.GetComponent(GdaiSceneObjectComposer.ResolveComponentType("EnemyDirector"));
            var so = new SerializedObject(director);
            var prop = so.FindProperty("enemyPrefab");
            Assert.IsNotNull(prop.objectReferenceValue, "EnemyDirector.enemyPrefab assigned");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), prop.objectReferenceValue);
        }
    }

    // Small folder helper mirroring the builder's private one (tests may need it standalone).
    internal static class GdaiEnemyPrefabBuilderTestUtil
    {
        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parts = assetFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
