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

                // Draft blocker collider: only for demo_draft_blocker physics that is NOT confirmed
                // (confirmed geometry is future scene_geometry — never promoted here).
                bool wantBlocker = e.physics != null && e.physics.kind == DraftBlockerKind && !e.physics.confirmed;
                var col = go.GetComponent<BoxCollider2D>();
                if (wantBlocker)
                {
                    if (col == null) col = Undo.AddComponent<BoxCollider2D>(go);
                    Undo.RecordObject(col, "GDAI · scene element draft collider");
                    col.isTrigger = false;
                    col.offset = Vector2.zero;
                    col.size = ResolveColliderSize(e, sprite);
                    blockers++;
                }
                else if (col != null)
                {
                    // no longer a draft blocker → remove our stale collider (idempotent)
                    Undo.DestroyObjectImmediate(col);
                }
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
        /// Collider world size: physics.w/h (canvas px → world) if given, else sprite world bounds,
        /// else a conservative fallback. (Y-flip affects position, not size.)
        /// </summary>
        private static Vector2 ResolveColliderSize(SceneElementDto e, Sprite sprite)
        {
            if (e.physics != null && e.physics.w > 0f && e.physics.h > 0f)
                return GdaiSceneAssemblyCoordinateUtility.CanvasSizeToWorld(e.physics.w, e.physics.h);
            if (sprite != null)
            {
                Vector3 b = sprite.bounds.size;
                if (b.x > 1e-3f && b.y > 1e-3f) return new Vector2(b.x, b.y);
            }
            return new Vector2(FallbackColliderWorldSize, FallbackColliderWorldSize);
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
