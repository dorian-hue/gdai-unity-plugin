using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;                 // GdaiEntitySpriteBinding marker
using GDAI.Bridge.Editor.LayerA;   // GdaiBundleProxyAsset (DTO)

// =====================================================================================
// UNITY-SCENE-BG-BIND-1 · Scene background placement (Editor, Layer B).
//
// The coherent-bundle backend now emits a project-level scene background as a manifest
// item (asset_type "image", role "scene_background", bucket "module-assets"; see
// _shared/code_gen/assetManifest.ts buildSceneBackgroundRef). unity-bundle-proxy resolves
// it to base64 and AssetPayloadImporter already writes+imports it as a Sprite under
// Assets/GDAI_Generated/Art/ — exactly like every other image. What was missing is the
// PLACEMENT: turning that imported Sprite into a visible background behind gameplay.
//
// This tool owns one deterministic object it creates itself: GDAI_SceneBackground
//   · SpriteRenderer at sortingOrder -100  → renders BEHIND Player/Enemy (order 0)
//   · NO collider (never added)
//   · scaled to cover the main camera view (perspective- and orthographic-aware)
//   · a GdaiEntitySpriteBinding marker (role "scene_background") as the idempotency anchor
//
// Discipline (mirrors GdaiAssetBindingUtility / Layer C0):
//   · scene_background has NO entity_id → it never enters GdaiImportedAssetRegistry;
//     selection is by manifest role, and the last one is persisted to a small pointer
//     next to the registry so the manual menu works standalone.
//   · NEVER touches Player/Enemy objects, their SpriteRenderers, sprites, colors or sorting
//     (that is GdaiSemanticSpriteBinder's contract — preserved untouched here).
//   · Undo supported · scene marked dirty but NEVER saved · refuses to run in Play Mode.
//   · Idempotent: re-running (menu or re-import) updates the same object in place, never
//     duplicates. No hardcoded ids/names/projects — data flows from the bundle DTO/pointer.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneBackgroundBinder
    {
        public const string BackgroundObjectName = "GDAI_SceneBackground";
        public const string BackgroundRole = "scene_background";

        // Behind Player/Enemy: they live on the default sorting layer at order 0 and never
        // set sorting (GdaiSemanticSpriteBinder). A clearly-negative order guarantees "behind"
        // without touching z or their renderers.
        private const int BackgroundSortingOrder = -100;

        // Used only when no main camera exists (headless / not-yet-built scene).
        private const float FallbackCoverageWorldHeight = 20f;
        private const float FallbackAspect = 16f / 9f;

        // Pointer lives beside the asset registry inside GDAI_Generated on purpose: the
        // coherent-bundle clean-replace wipes it and the next import regenerates it, so it can
        // never point at a stale bundle. It only records WHICH imported file is the background
        // (the file itself is the source of truth), letting the manual menu run without a DTO.
        public const string PointerDir = "Assets/GDAI_Generated/AssetRegistry";
        public const string PointerPath = PointerDir + "/gdai_scene_background.json";

        [Serializable]
        public class SceneBackgroundPointer
        {
            public int version = 1;
            public string generated_at;
            public string source_snapshot_id;
            public string unity_path;
            public string asset_id;
            public string source_project_id;
        }

        // ----------------------------------------------------------------------------
        // Selection (pure): pick the scene_background manifest entry by ROLE.
        // Never falls back to asset_type=="image" — that would grab entity sprites.
        // ----------------------------------------------------------------------------
        public static GdaiBundleProxyAsset SelectSceneBackground(List<GdaiBundleProxyAsset> assets)
        {
            if (assets == null) return null;
            return assets.FirstOrDefault(a =>
                a != null && a.role == BackgroundRole && !string.IsNullOrEmpty(a.unity_path));
        }

        // ----------------------------------------------------------------------------
        // Auto-hook entry: called from GdaiCoherentBundleWindow after a successful import.
        // Additive and defensive — a background problem must never break bundle import.
        // ----------------------------------------------------------------------------
        public static bool PlaceFromBundle(List<GdaiBundleProxyAsset> assets, string snapshotId, out string message)
        {
            message = null;
            var bg = SelectSceneBackground(assets);
            if (bg == null) { message = "no scene_background in bundle (nothing to place)"; return false; }

            // Persist the pointer first so the manual menu can re-place later without a DTO.
            WritePointer(bg, snapshotId, out _);
            string projectId = bg.source != null ? bg.source.project_id : null;
            return PlaceOrUpdate(bg.unity_path, bg.asset_id, projectId, out message);
        }

        // ------------------------------ menus ------------------------------

        [MenuItem("GDAI/Assets · Place Scene Background")]
        public static void PlaceMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Place Scene Background",
                    "Stop Play Mode first — scene objects should be placed in Edit Mode.", "OK");
                return;
            }

            var ptr = LoadPointer();
            if (ptr == null || string.IsNullOrEmpty(ptr.unity_path))
            {
                EditorUtility.DisplayDialog("GDAI · Place Scene Background",
                    "No scene background is available yet.\n\nImport a bundle that contains a " +
                    "generated scene background first (GDAI ▸ Unity Connector ▸ Import Latest Bundle). " +
                    "The background is placed automatically on import; this menu re-places the last one.",
                    "OK");
                return;
            }

            bool ok = PlaceOrUpdate(ptr.unity_path, ptr.asset_id, ptr.source_project_id, out string msg);
            EditorUtility.DisplayDialog("GDAI · Place Scene Background",
                (ok ? "Done.\n\n" : "Could not place.\n\n") + msg, "OK");
        }

        [MenuItem("GDAI/Assets · Validate Scene Background")]
        public static void ValidateMenu()
        {
            string report = Validate();
            Debug.Log("[GDAI][Assets][SceneBackground][Validate] " + report.Replace("\n", " | "));
            EditorUtility.DisplayDialog("GDAI · Validate Scene Background",
                report + "\n\n(dry run — nothing written)", "OK");
        }

        // ------------------------------ core ------------------------------

        /// <summary>
        /// Create-or-update the deterministic GDAI_SceneBackground object from an imported
        /// Sprite at <paramref name="unityPath"/>. Idempotent, Undo-able, never saves the scene.
        /// </summary>
        public static bool PlaceOrUpdate(string unityPath, string assetId, string projectId, out string message)
        {
            message = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { message = "cannot place scene background during Play Mode"; return false; }
            if (string.IsNullOrEmpty(unityPath))
            { message = "empty unity_path"; return false; }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityPath);
            if (sprite == null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(unityPath);
                message = tex != null
                    ? "texture present but not imported as Sprite: " + unityPath
                    : "background asset file missing: " + unityPath;
                return false;
            }

            var go = FindExistingBackground(out int duplicates);
            bool created = false;
            if (go == null)
            {
                go = new GameObject(BackgroundObjectName);
                Undo.RegisterCreatedObjectUndo(go, "GDAI · Create scene background");
                created = true;
            }
            else if (go.name != BackgroundObjectName)
            {
                Undo.RecordObject(go, "GDAI · Rename scene background");
                go.name = BackgroundObjectName;
            }

            // SpriteRenderer — get or add on OUR object only (never on gameplay objects).
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = Undo.AddComponent<SpriteRenderer>(go);
            Undo.RecordObject(sr, "GDAI · Assign scene background sprite");
            sr.sprite = sprite;
            sr.color = Color.white;
            sr.sortingLayerName = "Default";  // same layer as gameplay; order decides depth
            sr.sortingOrder = BackgroundSortingOrder;

            // Idempotency / lineage marker (project-level: no entity_id).
            var marker = go.GetComponent<GdaiEntitySpriteBinding>();
            if (marker == null) marker = Undo.AddComponent<GdaiEntitySpriteBinding>(go);
            Undo.RecordObject(marker, "GDAI · scene background marker");
            marker.entityId = null;
            marker.assetId = assetId;
            marker.worldEntityName = null;
            marker.role = BackgroundRole;

            // Position + cover-scale (perspective/orthographic aware).
            Undo.RecordObject(go.transform, "GDAI · scene background transform");
            PositionAndScaleToCover(go.transform, sprite);

            // Explicitly no collider: we never add one. Warn (don't mutate) if a stray one exists.
            bool strayCollider = go.GetComponent<Collider2D>() != null || go.GetComponent<Collider>() != null;

            EditorSceneManager.MarkSceneDirty(go.scene);

            message = (created ? "Created " : "Updated ") + BackgroundObjectName +
                      " · sprite '" + sprite.name + "' · sortingOrder " + BackgroundSortingOrder +
                      " · scale " + go.transform.localScale.x.ToString("0.##") +
                      "\nsource: " + unityPath +
                      (duplicates > 0 ? "\nWARNING: " + duplicates + " extra background object(s) found — using the first, please delete the others." : "") +
                      (strayCollider ? "\nWARNING: a collider is present on the background (task requires none) — remove it." : "") +
                      "\nScene marked dirty (not saved).";
            Debug.Log("[GDAI][Assets][SceneBackground] " + message);
            return true;
        }

        /// <summary>
        /// Find the existing background robustly: marker role first (survives rename / inactive),
        /// then the deterministic name. <paramref name="duplicates"/> = extra marker matches.
        /// </summary>
        private static GameObject FindExistingBackground(out int duplicates)
        {
            duplicates = 0;
            var markers = UnityEngine.Object
                .FindObjectsByType<GdaiEntitySpriteBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m != null && m.role == BackgroundRole)
                .ToList();
            if (markers.Count > 0)
            {
                duplicates = markers.Count - 1;
                return markers[0].gameObject;
            }
            return GameObject.Find(BackgroundObjectName);
        }

        private static void PositionAndScaleToCover(Transform t, Sprite sprite)
        {
            const float bgZ = 0f; // same plane as gameplay; sortingOrder handles draw order
            float coverH, coverW;
            Vector3 center;

            var cam = Camera.main;
            if (cam != null)
            {
                float dist = Mathf.Max(0.01f, Mathf.Abs(bgZ - cam.transform.position.z));
                coverH = cam.orthographic
                    ? cam.orthographicSize * 2f
                    : 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float aspect = cam.aspect > 0.01f ? cam.aspect : FallbackAspect;
                coverW = coverH * aspect;
                center = new Vector3(cam.transform.position.x, cam.transform.position.y, bgZ);
            }
            else
            {
                coverH = FallbackCoverageWorldHeight;
                coverW = coverH * FallbackAspect;
                center = new Vector3(0f, 0f, bgZ);
            }

            t.position = center;
            t.rotation = Quaternion.identity;

            // Sprite world size at scale 1 = (pixels / PPU). Uniform-scale to COVER (fill fully,
            // keep aspect, allow slight overflow) rather than stretch/distort the art.
            Vector3 size = sprite.bounds.size;
            float sx = size.x > 1e-4f ? coverW / size.x : 1f;
            float sy = size.y > 1e-4f ? coverH / size.y : 1f;
            float s = Mathf.Max(sx, sy);
            if (s <= 0f || float.IsNaN(s) || float.IsInfinity(s)) s = 1f;
            t.localScale = new Vector3(s, s, 1f);
        }

        // ------------------------------ validate (dry run) ------------------------------

        public static string Validate()
        {
            var lines = new List<string>();
            var ptr = LoadPointer();
            bool hasPtr = ptr != null && !string.IsNullOrEmpty(ptr.unity_path);
            lines.Add("Pointer: " + (hasPtr ? ptr.unity_path : "(none — import a bundle with a scene background)"));

            if (hasPtr)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ptr.unity_path);
                lines.Add("Imported sprite loads: " + (sprite != null ? "yes" : "NO (texture not a Sprite / missing)"));
            }

            var go = FindExistingBackground(out int dup);
            lines.Add("GDAI_SceneBackground in scene: " + (go != null ? "yes" : "no"));
            if (go != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                lines.Add("  SpriteRenderer: " + (sr != null
                    ? "yes · sprite=" + (sr.sprite != null ? sr.sprite.name : "none") +
                      " · sortingOrder=" + sr.sortingOrder + (sr.sortingOrder < 0 ? " (behind gameplay ✓)" : " (WARN: not negative)")
                    : "MISSING"));
                bool hasCol = go.GetComponent<Collider2D>() != null || go.GetComponent<Collider>() != null;
                lines.Add("  Collider: " + (hasCol ? "PRESENT (task requires none)" : "none ✓"));
                if (dup > 0) lines.Add("  WARNING: " + dup + " duplicate background object(s) present.");
            }
            return string.Join("\n", lines);
        }

        // ------------------------------ pointer IO (never throws) ------------------------------

        public static bool WritePointer(GdaiBundleProxyAsset bg, string snapshotId, out string error)
        {
            error = null;
            try
            {
                var ptr = new SceneBackgroundPointer
                {
                    generated_at = DateTime.UtcNow.ToString("o"),
                    source_snapshot_id = snapshotId ?? string.Empty,
                    unity_path = bg != null ? bg.unity_path : null,
                    asset_id = bg != null ? bg.asset_id : null,
                    source_project_id = bg != null && bg.source != null ? bg.source.project_id : null,
                };
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absDir = Path.GetFullPath(Path.Combine(projectRoot, PointerDir));
                Directory.CreateDirectory(absDir);
                File.WriteAllText(Path.Combine(absDir, "gdai_scene_background.json"),
                    JsonConvert.SerializeObject(ptr, Formatting.Indented));
                AssetDatabase.ImportAsset(PointerPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogWarning("[GDAI][Assets][SceneBackground] failed to write pointer: " + e.Message);
                return false;
            }
        }

        public static SceneBackgroundPointer LoadPointer()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absPath = Path.GetFullPath(Path.Combine(projectRoot, PointerPath));
                if (!File.Exists(absPath)) return null;
                return JsonConvert.DeserializeObject<SceneBackgroundPointer>(File.ReadAllText(absPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GDAI][Assets][SceneBackground] failed to load pointer: " + e.Message);
                return null;
            }
        }
    }
}
