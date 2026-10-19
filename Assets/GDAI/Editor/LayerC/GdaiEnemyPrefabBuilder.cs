// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-D3 · Enemy prefab builder (Editor, Layer C).
//
// Creates the OWNED default-enemy prefab the rev4 contract declares at
// enemy_prefab.asset_path — SpriteRenderer + Rigidbody2D + a concrete Collider2D +
// the GdaiGeneratedPlayableMarker — so EnemyDirector.enemyPrefab points at a visible,
// physics-bearing, HITTABLE asset. This is the create half; GdaiEnemyPrefabBinder is
// the (existing) sprite-rebind half. Discipline:
//   * path must be the contract's owned .prefab (never elsewhere).
//   * a prefab ALREADY at that path WITHOUT our marker is a human asset → refuse to
//     overwrite (fail closed); an owned one is updated in place → GUID stays stable.
//   * component types are resolved fail-closed via GdaiSceneObjectComposer (the exact
//     names the contract lists), never guessed.
//   * the collider is sized to the sprite so it enabled + non-zero + overlaps the
//     visible pixels (hittable); when no sprite is resolved yet it gets a sane unit
//     box that a later sprite-fit corrects — never a zero collider.
//   * never saves the scene here; the director binding marks the scene dirty only.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiEnemyPrefabBuilder
    {
        public const string OwnedPrefix = GdaiPlayableContract.OwnedPrefix; // Assets/GDAI_Project/Generated/
        private const float MinColliderExtent = 0.01f; // never a zero collider
        private const float DefaultBoxSize = 1f;        // sane unit box until a sprite is fit

        public class Result
        {
            public bool Ok;
            public string PrefabPath;
            public string PrefabGuid;
            public bool Created;
            public readonly List<string> Errors = new List<string>();
            public string Summary => Errors.Count == 0
                ? (Created ? "created " : "verified ") + PrefabPath + " (guid " + (PrefabGuid ?? "?") + ")"
                : string.Join("; ", Errors);
        }

        /// <summary>
        /// Create-or-verify the owned enemy prefab from the contract spec. If <paramref name="sprite"/>
        /// is non-null it is assigned and the collider is fit to it; otherwise a unit box is used.
        /// Refuses to overwrite a same-path prefab that is not GDAI-owned. Idempotent (stable GUID).
        /// </summary>
        public static Result Build(GdaiPlayableContract.EnemyPrefabSpec spec, Sprite sprite, string profileId, string snapshotId)
        {
            var r = new Result();
            if (spec == null) { r.Errors.Add("null enemy_prefab spec"); return r; }

            string path = spec.asset_path;
            if (string.IsNullOrEmpty(path) || !path.StartsWith(OwnedPrefix, StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.Ordinal)
                || path.Contains("..") || path.Contains("\\"))
            { r.Errors.Add("enemy_prefab.asset_path must be an owned .prefab under " + OwnedPrefix + ": " + path); return r; }
            r.PrefabPath = path;

            // resolve the exact component types the contract lists (fail-closed).
            var compTypes = new List<Type>();
            foreach (var name in spec.required_runtime_components ?? new List<string>())
            {
                var t = GdaiSceneObjectComposer.ResolveComponentType(name, out var outcome);
                if (t == null) { r.Errors.Add("enemy component '" + name + "' unresolved (" + outcome + ")"); return r; }
                compTypes.Add(t);
            }
            var colliderType = compTypes.FirstOrDefault(t => typeof(Collider2D).IsAssignableFrom(t));
            if (colliderType == null) { r.Errors.Add("enemy_prefab has no concrete Collider2D in required_runtime_components"); return r; }
            if (!compTypes.Any(t => t == typeof(SpriteRenderer))) { r.Errors.Add("enemy_prefab must require SpriteRenderer"); return r; }
            if (!compTypes.Any(t => t == typeof(Rigidbody2D))) { r.Errors.Add("enemy_prefab must require Rigidbody2D"); return r; }

            int layer = LayerMask.NameToLayer(spec.layer ?? "");
            if (layer < 0) { r.Errors.Add("enemy_prefab.layer '" + spec.layer + "' is not a defined layer in this project"); return r; }

            // same-path ownership gate — fail closed on ANY file already at the path that is not a
            // GDAI-owned prefab. Keying only on LoadAssetAtPath!=null would let an on-disk-but-not-yet
            // -imported human .prefab slip through and be overwritten, so also consult the GUID/file.
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool ownedExisting = existing != null && existing.GetComponent<GdaiGeneratedPlayableMarker>() != null;
            // On-disk presence is the authoritative "a file is already here" signal — it catches an
            // unimported human .prefab that LoadAssetAtPath cannot see. AssetPathToGUID is deliberately
            // NOT used: its path→GUID cache lags a just-deleted asset within the same editor session and
            // would falsely refuse a legitimately-clean path.
            bool fileOnDisk = File.Exists(Path.GetFullPath(Path.Combine(ProjectRoot(), path)));
            if ((existing != null || fileOnDisk) && !ownedExisting)
            { r.Errors.Add("a prefab already exists at " + path + " and is not GDAI-owned (unmarked human prefab or unimported file) — refusing to overwrite"); return r; }

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            // Build the content graph on a throwaway root, then save to the asset path.
            // For an existing owned prefab we edit its contents in place so the GUID never changes.
            GameObject root = null;
            bool usedContents = false;
            try
            {
                if (existing != null)
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    usedContents = true;
                }
                else
                {
                    root = new GameObject(spec.object_name);
                    r.Created = true;
                }

                if (root.name != spec.object_name) root.name = spec.object_name;
                root.layer = layer;

                foreach (var t in compTypes)
                    if (root.GetComponent(t) == null) root.AddComponent(t);

                var sr = root.GetComponent<SpriteRenderer>();
                if (sprite != null) { sr.sprite = sprite; sr.color = Color.white; }

                var col = (Collider2D)root.GetComponent(colliderType);
                col.enabled = true;
                FitColliderToSprite(col, sprite);

                var marker = root.GetComponent<GdaiGeneratedPlayableMarker>();
                if (marker == null) marker = root.AddComponent<GdaiGeneratedPlayableMarker>();
                marker.profileId = profileId;
                marker.snapshotId = snapshotId;
                marker.ownedRole = "enemy";

                var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saveOk);
                if (!saveOk || saved == null) { r.Errors.Add("failed to save prefab asset at " + path); return r; }
            }
            catch (Exception e) { r.Errors.Add("enemy prefab build threw: " + e.Message); return r; }
            finally
            {
                if (usedContents && root != null) PrefabUtility.UnloadPrefabContents(root);
                else if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
            }

            r.PrefabGuid = AssetDatabase.AssetPathToGUID(path);

            // post-verify the geometry contract on the saved asset.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { r.Errors.Add("post-verify: prefab did not load from " + path); return r; }
            var vcol = (Collider2D)asset.GetComponent(colliderType);
            if (vcol == null || !vcol.enabled) r.Errors.Add("post-verify: collider missing or disabled");
            else if (ColliderArea(vcol) <= 0f) r.Errors.Add("post-verify: collider has zero area");
            if (asset.GetComponent<GdaiGeneratedPlayableMarker>() == null) r.Errors.Add("post-verify: ownership marker missing");
            if (asset.layer != layer) r.Errors.Add("post-verify: layer not applied");

            r.Ok = r.Errors.Count == 0;
            return r;
        }

        /// <summary>
        /// Assign the built prefab to EnemyDirector.enemyPrefab on the unique EnemyManager scene
        /// object (contract director binding). Marks the scene dirty; never saves it.
        /// </summary>
        public static bool BindToDirector(string prefabPath, GdaiPlayableContract.EnemyPrefabSpec spec, out string error)
        {
            error = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { error = "prefab not found at " + prefabPath; return false; }

            var db = spec?.director_bindings?.FirstOrDefault();
            string managerObjectName = db?.@object ?? "EnemyManager";
            string field = db?.field ?? "enemyPrefab";
            string componentName = db?.component ?? "EnemyDirector";

            var manager = GdaiSceneObjectComposer.FindOwned(managerObjectName);
            if (manager == null) { error = "owned director object '" + managerObjectName + "' not found in scene (compose first)"; return false; }

            var directorType = GdaiSceneObjectComposer.ResolveComponentType(componentName, out _);
            if (directorType == null) { error = "director component '" + componentName + "' unresolved"; return false; }
            var director = manager.GetComponent(directorType);
            if (director == null) { error = managerObjectName + " has no " + componentName; return false; }

            var so = new SerializedObject(director);
            var prop = so.FindProperty(field);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
            { error = componentName + "." + field + " is not an object-reference field"; return false; }
            prop.objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.scene);
            return true;
        }

        // ---- helpers ----

        private static void FitColliderToSprite(Collider2D col, Sprite sprite)
        {
            if (col is BoxCollider2D box)
            {
                if (sprite != null)
                {
                    var b = sprite.bounds; // local-space world units
                    box.size = new Vector2(Mathf.Max(MinColliderExtent, b.size.x), Mathf.Max(MinColliderExtent, b.size.y));
                    box.offset = new Vector2(b.center.x, b.center.y);
                }
                else if (box.size.sqrMagnitude < MinColliderExtent)
                {
                    box.size = new Vector2(DefaultBoxSize, DefaultBoxSize);
                }
            }
            else if (col is CircleCollider2D circle)
            {
                if (sprite != null)
                {
                    var e = sprite.bounds.extents;
                    circle.radius = Mathf.Max(MinColliderExtent, Mathf.Max(e.x, e.y));
                    circle.offset = new Vector2(sprite.bounds.center.x, sprite.bounds.center.y);
                }
                else if (circle.radius < MinColliderExtent) circle.radius = DefaultBoxSize * 0.5f;
            }
            // other concrete colliders keep whatever non-zero default Unity gives them.
        }

        private static float ColliderArea(Collider2D col)
        {
            if (col is BoxCollider2D b) return b.size.x * b.size.y;
            if (col is CircleCollider2D c) return Mathf.PI * c.radius * c.radius;
            var bnds = col.bounds.size; // fallback: AABB area
            return bnds.x * bnds.y;
        }

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parts = assetFolder.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
