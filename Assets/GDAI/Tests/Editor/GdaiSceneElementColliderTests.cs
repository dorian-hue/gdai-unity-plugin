// T4 0J Gate B · B5 · Scene-element draft-collider resolution: fail-closed polygon + box→sprite_polygon
// transition + idempotency. Drives the REAL GdaiSceneAssemblyElements.ApplyDraftCollider (internal, visible
// via InternalsVisibleTo) over synthetic sprites — the shipped demo bundle carries no polygon intent, so these
// lanes need a synthetic fixture. A runtime Sprite.Create sprite has NO physics shape (GetPhysicsShapeCount==0
// → box lane); an imported opaque PNG gets an auto physics shape (count==1 → sprite_polygon lane).
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GDAI.Bridge.Editor.LayerB;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GDAI.Bridge.Editor.Tests
{
    public class GdaiSceneElementColliderTests
    {
        private const string ImportedSpritePath = "Assets/__gdai_b5_physsprite.png";
        private readonly List<Object> _spawned = new List<Object>();
        private bool _importedAssetUsed;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            if (_importedAssetUsed && File.Exists(ImportedSpritePath))
                AssetDatabase.DeleteAsset(ImportedSpritePath);
            _importedAssetUsed = false;
        }

        private GameObject NewGo()
        {
            var go = new GameObject("GDAI_SceneElement_test");
            _spawned.Add(go);
            return go;
        }

        private static Color32[] OpaquePixels(int n)
        {
            var px = new Color32[n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            return px;
        }

        /// <param name="withPhysicsShape">true → imported PNG (auto physics shape, GetPhysicsShapeCount==1,
        /// buildable polygon); false → runtime Sprite.Create (no physics shape, box lane).</param>
        private Sprite NewSprite(bool withPhysicsShape)
        {
            if (!withPhysicsShape)
            {
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                tex.SetPixels32(OpaquePixels(64)); tex.Apply();
                _spawned.Add(tex);
                var sp = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f);
                _spawned.Add(sp);
                return sp;   // GetPhysicsShapeCount()==0 → not buildable as polygon
            }

            // imported opaque PNG → Unity generates an auto physics shape from the alpha (count==1, >=3 pts).
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            t.SetPixels32(OpaquePixels(64)); t.Apply();
            var png = t.EncodeToPNG(); Object.DestroyImmediate(t);
            File.WriteAllBytes(ImportedSpritePath, png);
            _importedAssetUsed = true;
            AssetDatabase.ImportAsset(ImportedSpritePath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(ImportedSpritePath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(ImportedSpritePath);
        }

        private static SceneElementDto DraftBlocker(string colliderMode)
        {
            return new SceneElementDto
            {
                id = "e1",
                role = "obstacle",
                physics = new PhysicsDto
                {
                    kind = "demo_draft_blocker",
                    confirmed = false,
                    collider_mode = colliderMode,     // null/"box"/"polygon"
                    version = colliderMode != null ? 1 : 0,
                }
            };
        }

        private static int Count<T>(GameObject go) where T : Component => go.GetComponents<T>().Length;

        // ── B5-1 · a BOX-intent element with no sprite physics shape → a single BoxCollider2D (sprite bounds) ──
        [Test]
        public void BoxIntent_NoPhysicsShape_ResolvesToSingleBox()
        {
            var go = NewGo();
            var res = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker(null), NewSprite(false));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Box, res);
            Assert.AreEqual(1, Count<BoxCollider2D>(go), "exactly one BoxCollider2D");
            Assert.AreEqual(0, Count<PolygonCollider2D>(go), "no polygon");
        }

        // ── B5-2 · box→sprite_polygon TRANSITION: same object, sprite gains a physics shape on re-sync ──
        [Test]
        public void BoxToSpritePolygon_Transition_RemovesStaleBox()
        {
            var go = NewGo();
            // sync 1: no physics shape → BoxCollider2D
            var r1 = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker(null), NewSprite(false));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Box, r1);
            Assert.AreEqual(1, Count<BoxCollider2D>(go));
            Assert.AreEqual(0, Count<PolygonCollider2D>(go));

            // sync 2: sprite now HAS a physics shape → upgrade to PolygonCollider2D, stale box removed
            var r2 = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker(null), NewSprite(true));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Polygon, r2);
            Assert.AreEqual(1, Count<PolygonCollider2D>(go), "exactly one PolygonCollider2D after transition");
            Assert.AreEqual(0, Count<BoxCollider2D>(go), "stale BoxCollider2D removed on transition");
        }

        // ── B5-3 · EXPLICIT polygon intent + buildable sprite shape → PolygonCollider2D (no box) ──
        [Test]
        public void ExplicitPolygon_WithBuildableShape_ResolvesToPolygon()
        {
            var go = NewGo();
            var res = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker("polygon"), NewSprite(true));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Polygon, res);
            Assert.AreEqual(1, Count<PolygonCollider2D>(go));
            Assert.AreEqual(0, Count<BoxCollider2D>(go));
        }

        // ── B5-4 · FAIL-CLOSED: explicit polygon intent + UNBUILDABLE shape → UNRESOLVED, NO box substitute ──
        [Test]
        public void ExplicitPolygon_Unbuildable_IsUnresolved_NoBoxDowngrade()
        {
            LogAssert.Expect(LogType.Error, new Regex("collider mode: UNRESOLVED"));
            var go = NewGo();
            var res = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker("polygon"), NewSprite(false));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Unresolved, res);
            Assert.AreEqual(0, Count<BoxCollider2D>(go), "NO silent box downgrade for an unbuildable polygon");
            Assert.AreEqual(0, Count<PolygonCollider2D>(go), "no partial polygon");
        }

        // ── B5-5 · FAIL-CLOSED with a NULL sprite too (explicit polygon, no sprite at all) → UNRESOLVED ──
        [Test]
        public void ExplicitPolygon_NullSprite_IsUnresolved()
        {
            LogAssert.Expect(LogType.Error, new Regex("collider mode: UNRESOLVED"));
            var go = NewGo();
            var res = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker("polygon"), null);
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Unresolved, res);
            Assert.AreEqual(0, Count<BoxCollider2D>(go));
            Assert.AreEqual(0, Count<PolygonCollider2D>(go));
        }

        // ── B5-6 · IDEMPOTENCY: re-running the SAME resolution keeps exactly one collider (no duplicates) ──
        [Test]
        public void Idempotent_BoxThenBox_SingleCollider()
        {
            var go = NewGo();
            GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker(null), NewSprite(false));
            var r2 = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker(null), NewSprite(false));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Box, r2);
            Assert.AreEqual(1, Count<BoxCollider2D>(go), "no duplicate box on re-sync");
            Assert.AreEqual(0, Count<PolygonCollider2D>(go));
        }

        [Test]
        public void Idempotent_PolygonThenPolygon_SingleCollider()
        {
            var go = NewGo();
            GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker("polygon"), NewSprite(true));
            var r2 = GdaiSceneAssemblyElements.ApplyDraftCollider(go, DraftBlocker("polygon"), NewSprite(true));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.Polygon, r2);
            Assert.AreEqual(1, Count<PolygonCollider2D>(go), "no duplicate polygon on re-sync");
            Assert.AreEqual(0, Count<BoxCollider2D>(go));
        }

        // ── B5-7 · confirmed physics is NOT a draft blocker → NotWanted, colliders removed ──
        [Test]
        public void ConfirmedPhysics_IsNotWanted_NoCollider()
        {
            var go = NewGo();
            var dto = DraftBlocker(null);
            dto.physics.confirmed = true;   // confirmed → Unity does not build a draft collider
            var res = GdaiSceneAssemblyElements.ApplyDraftCollider(go, dto, NewSprite(true));
            Assert.AreEqual(GdaiSceneAssemblyElements.ColliderResolution.NotWanted, res);
            Assert.AreEqual(0, Count<BoxCollider2D>(go));
            Assert.AreEqual(0, Count<PolygonCollider2D>(go));
        }
    }
}
