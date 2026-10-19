using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;   // marker + kinds

// =====================================================================================
// UNITY-CONSUME-SCENE-ELEMENTS-1D · Consume explicit Flowcraft scene_elements (Editor, Layer B).
//
// Turns sceneAssembly.scene_elements[] (scene_layouts.scene_elements — props/obstacles/occluders
// placed in the web workbench, PHYSICALLY SEPARATE from entity_placements) into deterministic
// Unity objects that show the real generated PNG and, for demo_draft_blocker physics, carry a
// draft BoxCollider2D:
//   GDAI_SceneAssembly
//     └─ GDAI_SceneElement_<scene_element_id>   (SpriteRenderer + optional BoxCollider2D)
//
// Sprite resolution (reuses the existing payload import pipeline — NO new downloader):
//   assetManifest already emits role=scene_element and AssetPayloadImporter already wrote the PNG
//   to Assets/GDAI_Generated/Art/{name}_{id8}.{ext} where id8 = asset_id (dashes stripped, first 8).
//   We locate that imported Sprite by id8 (registry is entity-addressed, so it can't hold these).
//
// Discipline: reuses C1 coordinate utility + C2 root/ownership/idempotency helpers. Undo · scene
// dirty but never saved · refuses Play Mode. Never writes physics.confirmed, never creates
// scene_geometry, never touches ProjectSettings/TagManager/layers, never guesses from character
// sprites or entity_placements.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyElements
    {
        public const string ElementPrefix = "GDAI_SceneElement_";
        public const string ArtFolder = "Assets/GDAI_Generated/Art";
        private const string DraftBlockerKind = "demo_draft_blocker";
        private const float FallbackColliderWorldSize = 0.8f;
        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp" };

        // ── UNITY-SCENE-VISUAL-CLEANUP-1E · demo visual guards (Unity transform.localScale /
        //    SpriteRenderer.sortingOrder ONLY; NEVER written back to the bundle / scene_layouts) ──
        private const float DemoMinScale = 0.35f;
        private const float DemoMaxScale = 1.15f;
        private const int SceneElementSortingBase = -20;   // keep behind Player/Enemy(0), above background(-100)

        [MenuItem("GDAI/Scene · Place Scene Elements")]
        public static void PlaceSceneElementsMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Place Scene Elements",
                    "Stop Play Mode first — scene elements are placed in Edit Mode.", "OK");
                return;
            }
            var r = PlaceSceneElements();
            EditorUtility.DisplayDialog("GDAI · Place Scene Elements", r.dialog, "OK");
        }

        public static GdaiSceneAssemblySpawnMarkers.ApplyResult PlaceSceneElements()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string err))
                return Fail(err);
            if (dto.arena == null || dto.arena.width <= 0f || dto.arena.height <= 0f)
                return Fail("arena missing/invalid — cannot map scene elements.");
            if (dto.scene_elements == null || dto.scene_elements.Count == 0)
                return Fail("no scene_elements in bundle — nothing to place " +
                            "(add scene elements in the web Design Studio, then re-import the bundle).");

            var root = GdaiSceneAssemblySpawnMarkers.EnsureRoot(dto);
            int created = 0, updated = 0, withSprite = 0, missingSprite = 0, blockers = 0;
            var keepNames = new HashSet<string>();

            foreach (var e in dto.scene_elements)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;

                string name = ElementPrefix + e.id;
                keepNames.Add(name);

                var go = GdaiSceneAssemblySpawnMarkers.UpsertChild(
                    root, name, GdaiSceneAssemblyKind.SceneElement, e.id, e.role, dto, out bool wasCreated);
                if (wasCreated) created++; else updated++;

                // Position (shared C1 converter) + scale + rotation.
                Undo.RecordObject(go.transform, "GDAI · position scene element");
                go.transform.position = GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(e.x, e.y, dto.arena.width, dto.arena.height);
                go.transform.rotation = Quaternion.identity;
                // 1E demo visual clamp: bundle scale can be oversize (web placement value); clamp for
                // demo presentability ONLY — bundle/scene_layouts unchanged, never written back.
                float rawScale = e.scale > 0f ? e.scale : 1f;
                float s = Mathf.Clamp(rawScale, DemoMinScale, DemoMaxScale);
                go.transform.localScale = new Vector3(s, s, 1f);

                // Sprite: locate the already-imported Art/ PNG by asset_id id8.
                Sprite sprite = null;
                if (!string.IsNullOrEmpty(e.asset_id))
                    sprite = ResolveArtSpriteByAssetId(e.asset_id, out _);

                var sr = go.GetComponent<SpriteRenderer>();
                if (sprite != null)
                {
                    if (sr == null) sr = Undo.AddComponent<SpriteRenderer>(go);
                    Undo.RecordObject(sr, "GDAI · scene element sprite");
                    sr.sprite = sprite;                 // do NOT mutate the imported sprite asset
                    sr.color = Color.white;
                    sr.sortingLayerName = "Default";
                    // 1E: keep scene elements behind Player/Enemy (order 0) so characters stay visible,
                    // above background (-100); preserve z_index relative ordering among elements.
                    sr.sortingOrder = Mathf.Clamp(SceneElementSortingBase + e.z_index, -90, -1);
                    withSprite++;
                }
                else
                {
                    missingSprite++;
                    Debug.LogWarning("[GDAI][Scene][SceneElements] No imported sprite for scene element '" + e.id +
                                     "' (asset_id=" + (e.asset_id ?? "null") + "). Object created without SpriteRenderer; " +
                                     "re-import a bundle whose assetManifest carries this scene_element image.");
                }

                // Draft blocker collider (priority chain; consumes GDAI physics metadata when present,
                // else sprite-fit fallback). Only for demo_draft_blocker physics that is NOT confirmed.
                if (ApplyDraftCollider(go, e, sprite)) blockers++;
            }

            int pruned = GdaiSceneAssemblySpawnMarkers.PruneStaleChildren(root, keepNames, GdaiSceneAssemblyKind.SceneElement);

            EditorSceneManager.MarkSceneDirty(root.scene);
            string msg = "Scene elements: " + created + " created, " + updated + " updated, " + pruned + " stale removed.\n" +
                         "With sprite: " + withSprite + " · missing sprite: " + missingSprite + " · draft blockers: " + blockers + ".\n" +
                         "physics.confirmed never modified · no ProjectSettings/layers touched · scene marked dirty (not saved).";
            Debug.Log("[GDAI][Scene][SceneElements] " + msg);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = true, dialog = msg };
        }

        /// <summary>
        /// Draft blocker collider — priority: (A) metadata polygon [DEFERRED: point→local mapping is
        /// pending GDAI 1G spec + a real bundle, so it is recognized but NOT faked] → (B) metadata box
        /// (auto_alpha_bounds) → (C) sprite physics shape (PolygonCollider2D, tight fit) → (D) sprite
        /// bounds box (center offset) → (E) 0.8×0.8. Only for demo_draft_blocker physics that is NOT
        /// confirmed; never writes physics.confirmed. Idempotent: removes the stale collider of the
        /// other type when switching. Returns true if a collider is present after the call.
        /// </summary>
        private static bool ApplyDraftCollider(GameObject go, SceneElementDto e, Sprite sprite)
        {
            bool wantBlocker = e.physics != null && e.physics.kind == DraftBlockerKind && !e.physics.confirmed;
            if (!wantBlocker)
            {
                RemoveAll<BoxCollider2D>(go);
                RemoveAll<PolygonCollider2D>(go);
                return false;
            }

            var p = e.physics;

            // ★1F1 · only trust metadata carrying NEW GDAI 1G collision fields. Legacy source-less w/h
            //        (e.g. 80×80 → 0.8) is a hint, NOT SSOT → never build a metadata box from it.
            bool trustedMeta = !string.IsNullOrEmpty(p.collider_mode) || !string.IsNullOrEmpty(p.source) || p.version > 0;

            // A · trusted metadata polygon — recognized but NOT faked (point→local mapping pending 1G spec).
            if (trustedMeta && (p.collider_mode == "polygon" || p.shape == "polygon") && p.points != null && p.points.Count >= 3)
                Debug.Log("[GDAI][Scene][SceneElements] element '" + e.id + "' carries polygon collider metadata (" +
                          p.points.Count + " pts); point→local mapping pending GDAI 1G spec — using sprite-fit fallback for now.");

            // B · trusted metadata box (new GDAI 1G collision metadata, e.g. auto_alpha_bounds).
            if (trustedMeta && p.w > 0f && p.h > 0f)
            {
                RemoveAll<PolygonCollider2D>(go);
                var box = EnsureSingle<BoxCollider2D>(go);
                Undo.RecordObject(box, "GDAI · scene element box collider (metadata)");
                box.isTrigger = false;
                box.size = GdaiSceneAssemblyCoordinateUtility.CanvasSizeToWorld(p.w, p.h);
                box.offset = new Vector2(p.offset_x / GdaiSceneAssemblyCoordinateUtility.PpuWorld,
                                         -p.offset_y / GdaiSceneAssemblyCoordinateUtility.PpuWorld);
                Debug.Log("[GDAI][Scene][SceneElements] collider mode: metadata_box · element '" + e.id + "'.");
                return true;
            }

            // C · sprite physics shape → PolygonCollider2D (tight fit; removes ALL stale boxes).
            if (sprite != null && TryBuildSpriteShapePolygon(go, sprite))
            {
                Debug.Log("[GDAI][Scene][SceneElements] collider mode: sprite_polygon · element '" + e.id + "'.");
                return true;
            }

            // D · sprite bounds box (covers the visible image; center offset handles non-center pivot).
            if (sprite != null)
            {
                Vector3 b = sprite.bounds.size;
                if (b.x > 1e-3f && b.y > 1e-3f)
                {
                    RemoveAll<PolygonCollider2D>(go);
                    var box = EnsureSingle<BoxCollider2D>(go);
                    Undo.RecordObject(box, "GDAI · scene element sprite-bounds collider");
                    box.isTrigger = false;
                    Vector3 c = sprite.bounds.center;
                    box.size = new Vector2(b.x, b.y);
                    box.offset = new Vector2(c.x, c.y);
                    Debug.Log("[GDAI][Scene][SceneElements] collider mode: sprite_bounds_box · element '" + e.id + "'.");
                    return true;
                }
            }

            // E · last fallback: 0.8×0.8 (no trusted metadata, no usable sprite).
            RemoveAll<PolygonCollider2D>(go);
            var fb = EnsureSingle<BoxCollider2D>(go);
            Undo.RecordObject(fb, "GDAI · scene element fallback collider");
            fb.isTrigger = false;
            fb.offset = Vector2.zero;
            fb.size = new Vector2(FallbackColliderWorldSize, FallbackColliderWorldSize);
            Debug.LogWarning("[GDAI][Scene][SceneElements] collider mode: fallback_0_8 · no trusted metadata/no sprite for element '" + e.id + "'.");
            return true;
        }

        /// <summary>
        /// Build a PolygonCollider2D from the sprite's physics shape (sprite-local space → scales with
        /// the object, consistent with the 1E visual scale). False if the sprite has no usable shape
        /// (&lt;3 points). Removes a stale BoxCollider2D on success.
        /// </summary>
        private static bool TryBuildSpriteShapePolygon(GameObject go, Sprite sprite)
        {
            if (sprite.GetPhysicsShapeCount() <= 0) return false;
            var pts = new List<Vector2>();
            sprite.GetPhysicsShape(0, pts);
            if (pts.Count < 3) return false;

            RemoveAll<BoxCollider2D>(go);   // polygon chosen → remove ALL stale boxes (incl legacy 0.8)
            var poly = EnsureSingle<PolygonCollider2D>(go);
            Undo.RecordObject(poly, "GDAI · scene element sprite-shape collider");
            poly.isTrigger = false;
            poly.pathCount = 1;
            poly.SetPath(0, pts);
            return true;
        }

        /// <summary>Remove ALL components of type T (Undo-registered). Ensures no stale collider lingers.</summary>
        private static void RemoveAll<T>(GameObject go) where T : Component
        {
            var comps = go.GetComponents<T>();
            for (int i = 0; i < comps.Length; i++)
                if (comps[i] != null) Undo.DestroyObjectImmediate(comps[i]);
        }

        /// <summary>Return a single T on the object: drops any duplicates, adds one if none exists.</summary>
        private static T EnsureSingle<T>(GameObject go) where T : Component
        {
            var comps = go.GetComponents<T>();
            if (comps.Length == 0) return Undo.AddComponent<T>(go);
            for (int i = 1; i < comps.Length; i++)
                if (comps[i] != null) Undo.DestroyObjectImmediate(comps[i]);
            return comps[0];
        }

        /// <summary>
        /// Find the imported Sprite for a scene_element by asset_id. The assetManifest names the file
        /// {name}_{id8}.{ext} (id8 = asset_id dashes-stripped, first 8), imported under Art/. Registry
        /// is entity-addressed and cannot hold scene_elements, so we scan imported Sprites by id8.
        /// </summary>
        public static Sprite ResolveArtSpriteByAssetId(string assetId, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(assetId)) { reason = "empty_asset_id"; return null; }
            if (!AssetDatabase.IsValidFolder(ArtFolder)) { reason = "art_folder_missing"; return null; }

            string id8 = GdaiSceneAssemblyModels.ShortId(assetId);
            string needleEnd = "_" + id8;
            string needleMid = "_" + id8 + "_";

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!IsImageExt(path)) continue;
                string stem = Path.GetFileNameWithoutExtension(path);
                if (stem.EndsWith(needleEnd) || stem.Contains(needleMid))
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null) return sprite;
                }
            }
            reason = "no_imported_sprite_for_id8:" + id8;
            return null;
        }

        private static bool IsImageExt(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            foreach (var e in ImageExts) if (e == ext) return true;
            return false;
        }

        private static GdaiSceneAssemblySpawnMarkers.ApplyResult Fail(string reason)
        {
            Debug.LogWarning("[GDAI][Scene][SceneElements] FAIL: " + reason);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = false, dialog = "FAIL\n\n" + reason };
        }
    }
}
