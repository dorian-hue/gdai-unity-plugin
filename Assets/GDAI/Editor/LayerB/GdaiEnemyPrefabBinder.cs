using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using GDAI.Bridge;                 // GdaiEntitySpriteBinding marker
using GDAI.Bridge.Editor.LayerA;   // GdaiImportedAssetRegistry

// =====================================================================================
// ASSET-VERTICAL-2B · Default Enemy Prefab Binding.
// Binds the enemy role's adopted sprite to the persistent prefab asset referenced by the
// scene's EnemyManager.enemyPrefab, so runtime Enemy(Clone) objects spawn wearing the
// resolved default-enemy sprite instead of the placeholder square.
//
// The existing GdaiSemanticSpriteBinder binds enemy role to SCENE enemy instances (TestEnemy)
// but never rewrites the prefab asset — that is the Link-2 gap this tool closes.
//
// Zero-mutation Validate + guarded Bind (explicit confirm dialog, filesystem backup, exact
// path/role/sprite shown before any write). Never scans/rewrites other prefabs, never edits
// generated C#, never saves the scene, never runs on Import. Reuses the existing role→entity
// →sprite resolution; no hardcoded ids/names.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiEnemyPrefabBinder
    {
        private const string EnemyComponentType = "EnemyDirector";
        private const string EnemyManagerName = "EnemyManager";
        private const string EnemyCanonicalRole = "enemy_archetype";
        private const string BackupRoot = ".gdai/prefab_backups"; // OUTSIDE Assets so Unity never imports the backup

        private enum BindPath { A_BindPrefab, B_NoSpriteRenderer, C_NotPersistent, StopNoTarget }

        /// <summary>
        /// UNITY-BIND-FIX-1 · Result of the reusable, no-dialog prefab bind used by the semantic
        /// binder's Apply flow. NoRuntimePrefab = there is nothing to bind (scene object / no
        /// manager) — legitimate. Failed = a runtime prefab exists but could not be bound → the
        /// caller MUST count this as unresolved (never a silent scene-only false-pass).
        /// </summary>
        public enum PrefabBindResult { Bound, AlreadyBound, NoRuntimePrefab, Failed }

        /// <summary>
        /// Bind an already-resolved (entityId, assetId, sprite) onto the EnemyManager.enemyPrefab
        /// asset — no dialogs (for batch use by GdaiSemanticSpriteBinder.Apply). Creates a backup
        /// before writing. Refuses to run in Play Mode. Never resolves the role/sprite itself.
        /// </summary>
        public static PrefabBindResult BindEnemyPrefabSprite(string entityId, string assetId, Sprite sprite,
            bool makeBackup, out string prefabPath, out string reason)
        {
            prefabPath = null; reason = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { reason = "cannot bind prefab during Play Mode — stop Play Mode and re-run"; return PrefabBindResult.Failed; }
            if (sprite == null) { reason = "enemy sprite unresolved"; return PrefabBindResult.Failed; }

            Type enemyType = FindMonoBehaviourType(EnemyComponentType);
            if (enemyType == null) { reason = $"'{EnemyComponentType}' type not found"; return PrefabBindResult.NoRuntimePrefab; }

            var comps = GdaiLayerBSceneQuery.FindSceneComponents(enemyType);
            var managers = comps.Where(HasEnemyPrefabAssigned).ToList();
            Component manager = managers.Count == 1 ? managers[0]
                              : managers.FirstOrDefault(c => c.gameObject.name == EnemyManagerName);
            if (manager == null) { reason = "no EnemyManager with an assigned enemyPrefab"; return PrefabBindResult.NoRuntimePrefab; }

            var so = new SerializedObject(manager);
            var prop = so.FindProperty("enemyPrefab");
            var prefabRef = prop != null ? prop.objectReferenceValue as GameObject : null;
            if (prefabRef == null) { reason = "enemyPrefab not assigned"; return PrefabBindResult.NoRuntimePrefab; }
            if (!EditorUtility.IsPersistent(prefabRef) || !PrefabUtility.IsPartOfPrefabAsset(prefabRef))
            { reason = "enemyPrefab is a scene object, not a persistent asset"; return PrefabBindResult.NoRuntimePrefab; }

            prefabPath = AssetDatabase.GetAssetPath(prefabRef);
            if (string.IsNullOrEmpty(prefabPath)) { reason = "cannot resolve prefab path"; return PrefabBindResult.Failed; }
            var sr = prefabRef.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null) { reason = "prefab has no SpriteRenderer"; return PrefabBindResult.Failed; }

            var existing = sr.GetComponent<GdaiEntitySpriteBinding>();
            if (sr.sprite == sprite && existing != null && existing.entityId == entityId && existing.role == EnemyCanonicalRole)
                return PrefabBindResult.AlreadyBound;

            if (makeBackup)
            {
                try { Backup(prefabPath); }
                catch (Exception e) { reason = "backup failed: " + e.Message; return PrefabBindResult.Failed; }
            }
            string err = MutateCore(prefabPath, sprite, entityId, assetId);
            if (err != null) { reason = err; return PrefabBindResult.Failed; }
            return PrefabBindResult.Bound;
        }

        private sealed class Analysis
        {
            public bool ok;                 // ready to bind (Path A) — everything resolved
            public BindPath path;
            public string stop;             // non-null → cannot bind; human-readable reason
            public readonly List<string> notes = new List<string>();

            public string role, entityId, assetId, spriteName;
            public Sprite sprite;

            public GameObject prefabGo;     // the persistent prefab asset root
            public string prefabPath, prefabGuid;
            public SpriteRenderer prefabRenderer;
            public string currentSpriteName;
            public bool alreadyBound;
        }

        // ------------------------------ menus ------------------------------

        [MenuItem("GDAI/Assets · Validate Default Enemy Prefab Binding")]
        public static void ValidateMenu()
        {
            var a = Analyze();
            Debug.Log("[GDAI][Assets][EnemyPrefabBinding][Validate] " + OneLine(a));
            EditorUtility.DisplayDialog("GDAI · Validate Default Enemy Prefab Binding", Report(a, "(dry run — nothing written)"), "OK");
        }

        [MenuItem("GDAI/Assets · Bind Default Enemy Prefab")]
        public static void BindMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab",
                    "Stop Play Mode first — prefab assets cannot be edited during Play Mode.", "OK");
                return;
            }
            var a = Analyze();
            if (!a.ok)
            {
                EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab",
                    Report(a, "Cannot bind — see above. Nothing was written."), "OK");
                return;
            }
            if (a.alreadyBound)
            {
                EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab",
                    Report(a, "Already bound (same sprite + marker). Nothing to do — no backup created."), "OK");
                return;
            }

            // Routed through the central confirmation policy (C5): interactive/production ALWAYS shows this
            // exact dialog; a headless A4 run may auto-approve ONLY the ReplaceEnemyPrefab kind for the exact
            // injected operation identity. Backup + Mutate still run unchanged after approval.
            bool go = GdaiEditorConfirmationPolicy.Confirm(GdaiConfirmationKind.ReplaceEnemyPrefab,
                "GDAI · Bind Default Enemy Prefab",
                "Write the enemy sprite into the prefab asset? A backup is created first; Undo does not cover asset-file edits, so keep the backup.\n\n" +
                $"role: {a.role} → {EnemyCanonicalRole}\n" +
                $"entity: {Short(a.entityId)}\n" +
                $"sprite: {a.spriteName}\n" +
                $"prefab: {a.prefabPath}\n" +
                $"previous sprite: {a.currentSpriteName}\n" +
                $"backup: {BackupRoot}/<timestamp>/{Path.GetFileName(a.prefabPath)}",
                "Backup & Bind", "Cancel");
            if (!go) return;

            string backupRel;
            try { backupRel = Backup(a.prefabPath); }
            catch (Exception e)
            {
                Debug.LogError("[GDAI][Assets][EnemyPrefabBinding][Backup] FAILED — aborting, nothing mutated: " + e.Message);
                EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab", "Backup failed — nothing was written.\n\n" + e.Message, "OK");
                return;
            }
            Debug.Log($"[GDAI][Assets][EnemyPrefabBinding][Backup] {a.prefabPath} → {backupRel}");

            string mutateError = Mutate(a);
            if (mutateError != null)
            {
                Debug.LogError("[GDAI][Assets][EnemyPrefabBinding][Bind] write failed: " + mutateError);
                EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab",
                    $"Write failed after backup. Restore from {backupRel} if needed.\n\n{mutateError}", "OK");
                return;
            }

            Debug.Log("[GDAI][Assets][EnemyPrefabBinding][Bind] " +
                      $"prefab={a.prefabPath} entity={a.entityId} sprite={a.spriteName} previous={a.currentSpriteName} backup={backupRel} bound=true");
            EditorUtility.DisplayDialog("GDAI · Bind Default Enemy Prefab",
                $"Bound.\n\nprefab: {a.prefabPath}\nsprite: {a.spriteName}\nmarker role: {EnemyCanonicalRole}\nbackup: {backupRel}\n\n" +
                "Enter Play — Enemy(Clone) should now spawn with the enemy sprite. If it still shows the placeholder, the generated spawn code may overwrite the sprite (STOP-D — report, do not patch here).", "OK");
        }

        // ------------------------------ analysis (read-only) ------------------------------

        private static Analysis Analyze()
        {
            var a = new Analysis();

            // 1 · Resolve enemy role → entity → sprite via the EXISTING contract (no hardcoded ids).
            var map = GdaiSemanticRoleMap.Load(out string loadError);
            if (map == null) { a.stop = "No semantic role map — " + loadError + ". Apply Semantic Sprite Bindings / create the contract first."; return a; }

            var enemyEntry = map.roles.FirstOrDefault(e => e != null && !string.IsNullOrEmpty(e.role)
                                && GdaiSemanticSpriteBinder.CanonicalRole(e.role.Trim()) == EnemyCanonicalRole);
            if (enemyEntry == null) { a.stop = "Role map has no enemy role entry (enemy/default_enemy/enemy_archetype)."; return a; }
            a.role = enemyEntry.role.Trim();
            a.assetId = enemyEntry.asset_id;

            if (!GdaiSemanticRoleResolver.TryGetEntityIdForRole(a.role, out a.entityId, out string roleReason))
            { a.stop = $"Enemy role not grounded: {roleReason}"; return a; }
            if (!GdaiImportedAssetRegistry.TryGetSpriteForEntity(a.entityId, out a.sprite, out string spriteReason))
            { a.stop = $"Enemy sprite unresolved: {spriteReason}"; return a; }
            a.spriteName = a.sprite != null ? a.sprite.name : "(null)";

            // 2 · Locate EnemyManager.enemyPrefab (persistent prefab asset).
            Type enemyType = FindMonoBehaviourType(EnemyComponentType);
            if (enemyType == null) { a.stop = $"'{EnemyComponentType}' type not found — import a coherent bundle first."; return a; }

            var comps = GdaiLayerBSceneQuery.FindSceneComponents(enemyType);
            if (comps.Count == 0) { a.stop = $"No {EnemyComponentType} in the open scene (prepare the scene first)."; return a; }

            var managers = comps.Where(HasEnemyPrefabAssigned).ToList();
            Component manager = null;
            if (managers.Count == 1) manager = managers[0];
            else if (managers.Count > 1) manager = managers.FirstOrDefault(c => c.gameObject.name == EnemyManagerName);
            if (manager == null) { a.stop = "Could not uniquely identify the EnemyManager with an assigned enemyPrefab."; return a; }

            var so = new SerializedObject(manager);
            var prop = so.FindProperty("enemyPrefab");
            var prefabRef = prop != null ? prop.objectReferenceValue as GameObject : null;
            if (prefabRef == null) { a.stop = "EnemyManager.enemyPrefab is not assigned."; return a; }

            // 3 · Persistent asset? (Path C = scene object → scene binder already covers it.)
            if (!EditorUtility.IsPersistent(prefabRef) || !PrefabUtility.IsPartOfPrefabAsset(prefabRef))
            {
                a.path = BindPath.C_NotPersistent;
                a.stop = "enemyPrefab points to a scene object, not a persistent prefab asset — scene-scope binding already covers it; no prefab mutation needed.";
                return a;
            }
            a.prefabGo = prefabRef;
            a.prefabPath = AssetDatabase.GetAssetPath(prefabRef);
            a.prefabGuid = AssetDatabase.AssetPathToGUID(a.prefabPath);
            if (string.IsNullOrEmpty(a.prefabPath)) { a.stop = "Could not resolve the prefab asset path."; return a; }

            // 4 · SpriteRenderer present? (Path B = do not invent prefab structure.)
            a.prefabRenderer = prefabRef.GetComponentInChildren<SpriteRenderer>(true);
            if (a.prefabRenderer == null)
            {
                a.path = BindPath.B_NoSpriteRenderer;
                a.stop = "Prefab has no SpriteRenderer and no existing visual target — not inventing prefab structure (STOP-B).";
                return a;
            }
            a.currentSpriteName = a.prefabRenderer.sprite != null ? a.prefabRenderer.sprite.name : "(none)";

            // 5 · Idempotency: same sprite + matching marker already present?
            var existing = a.prefabRenderer.GetComponent<GdaiEntitySpriteBinding>();
            a.alreadyBound = a.prefabRenderer.sprite == a.sprite && existing != null
                             && existing.entityId == a.entityId && existing.role == EnemyCanonicalRole;

            a.path = BindPath.A_BindPrefab;
            a.ok = true;
            a.notes.Add($"Ready: {a.prefabPath} → sprite '{a.spriteName}' (was '{a.currentSpriteName}').");
            return a;
        }

        // ------------------------------ mutation (guarded) ------------------------------

        private static string Backup(string prefabPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string srcAbs = Path.GetFullPath(Path.Combine(projectRoot, prefabPath));
            string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string destDirAbs = Path.GetFullPath(Path.Combine(projectRoot, BackupRoot, ts));
            Directory.CreateDirectory(destDirAbs);

            string fileName = Path.GetFileName(prefabPath);
            File.Copy(srcAbs, Path.Combine(destDirAbs, fileName), false);
            if (File.Exists(srcAbs + ".meta")) File.Copy(srcAbs + ".meta", Path.Combine(destDirAbs, fileName + ".meta"), false);

            return $"{BackupRoot}/{ts}/{fileName}";
        }

        private static string Mutate(Analysis a) => MutateCore(a.prefabPath, a.sprite, a.entityId, a.assetId);

        private static string MutateCore(string prefabPath, Sprite sprite, string entityId, string assetId)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                var sr = root.GetComponentInChildren<SpriteRenderer>(true);
                if (sr == null) return "SpriteRenderer disappeared on load.";

                sr.sprite = sprite;
                sr.color = Color.white; // placeholders are tinted; reset so the real sprite isn't dyed

                var marker = sr.GetComponent<GdaiEntitySpriteBinding>();
                if (marker == null) marker = sr.gameObject.AddComponent<GdaiEntitySpriteBinding>();
                marker.entityId = entityId;
                marker.assetId = assetId;
                marker.worldEntityName = null;      // names are not binding data
                marker.role = EnemyCanonicalRole;   // canonical, matching the scene binder's convention

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return null;
            }
            catch (Exception e) { return e.Message; }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // ------------------------------ helpers ------------------------------

        private static bool HasEnemyPrefabAssigned(Component c)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty("enemyPrefab");
            return p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null;
        }

        private static Type FindMonoBehaviourType(string typeName)
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (t.Name == typeName) return t;
            return null;
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "(none)" : (id.Length > 8 ? id.Substring(0, 8) : id);

        private static string OneLine(Analysis a) =>
            a.ok ? $"path=A role={a.role} entity={Short(a.entityId)} sprite={a.spriteName} prefab={a.prefabPath} alreadyBound={a.alreadyBound}"
                 : $"blocked path={a.path} reason={a.stop}";

        private static string Report(Analysis a, string footer)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ASSET-VERTICAL-2B · Default Enemy Prefab Binding");
            sb.AppendLine();
            sb.AppendLine($"enemy role: {(a.role ?? "(unresolved)")} → {EnemyCanonicalRole}");
            sb.AppendLine($"entity: {(a.entityId != null ? Short(a.entityId) : "(unresolved)")}");
            sb.AppendLine($"sprite: {(a.spriteName ?? "(unresolved)")}");
            if (a.prefabPath != null)
            {
                sb.AppendLine($"prefab: {a.prefabPath}");
                sb.AppendLine($"current sprite: {a.currentSpriteName ?? "(unknown)"}");
                sb.AppendLine($"expected after bind: {a.spriteName}");
                sb.AppendLine($"already bound: {a.alreadyBound}");
            }
            if (a.stop != null) sb.AppendLine("\nBLOCKED: " + a.stop);
            foreach (var n in a.notes) sb.AppendLine(n);
            if (!string.IsNullOrEmpty(footer)) sb.AppendLine("\n" + footer);
            return sb.ToString().TrimEnd();
        }
    }
}
