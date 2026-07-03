using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-2 · Sprite preview / binding utility (Editor).
//
// EXPLICITLY a preview/materialization path — NOT gameplay role binding. It proves the
// registry → Sprite → SpriteRenderer chain on objects it owns, under one root it owns:
//   GDAI_ImportedAssetPreview
//     └── one child per registry entry (SpriteRenderer + GdaiEntitySpriteBinding)
//
// Discipline (mirrors Layer C0 conventions):
//   · never touches objects outside its own root
//   · re-running updates in place (matched by GdaiEntitySpriteBinding.entityId — id, not name)
//   · Undo supported · scene marked dirty but NEVER saved
//   · semantic player/enemy binding is deliberately absent (no reliable mapping exists yet;
//     that contract is the next task, not something to fake here).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public static class GdaiAssetBindingUtility
    {
        public const string PreviewRootName = "GDAI_ImportedAssetPreview";
        private const float Spacing = 2f;

        [MenuItem("GDAI/Assets · Build Imported Sprite Preview")]
        public static void BuildPreviewMenu()
        {
            var summary = BuildPreview();
            EditorUtility.DisplayDialog("GDAI · Imported Sprite Preview", summary, "OK");
        }

        /// <summary>Creates/updates the preview root from the registry. Returns a human summary.</summary>
        public static string BuildPreview()
        {
            var entries = GdaiImportedAssetRegistry.All();
            if (entries.Count == 0)
            {
                return "Registry is missing or empty.\n\nRun GDAI ▸ Unity Connector ▸ Import Latest Bundle first " +
                       "(the registry is generated automatically after asset import).";
            }

            var root = GameObject.Find(PreviewRootName);
            if (root == null)
            {
                root = new GameObject(PreviewRootName);
                Undo.RegisterCreatedObjectUndo(root, "GDAI · Create imported asset preview root");
            }

            // Existing bindings under the root, keyed by entity id (id-matching, never names).
            var existing = new Dictionary<string, GdaiEntitySpriteBinding>();
            foreach (var b in root.GetComponentsInChildren<GdaiEntitySpriteBinding>(true))
            {
                if (b != null && !string.IsNullOrEmpty(b.entityId) && !existing.ContainsKey(b.entityId))
                    existing[b.entityId] = b;
            }

            int applied = 0;
            var unresolved = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.entity_id)) continue;

                GdaiEntitySpriteBinding binding;
                GameObject go;
                if (existing.TryGetValue(entry.entity_id, out binding))
                {
                    go = binding.gameObject;
                    Undo.RecordObject(go.transform, "GDAI · Update sprite preview");
                }
                else
                {
                    go = new GameObject(SafeName(entry.world_entity_name, entry.entity_id));
                    Undo.RegisterCreatedObjectUndo(go, "GDAI · Create sprite preview");
                    go.transform.SetParent(root.transform, false);
                    binding = go.AddComponent<GdaiEntitySpriteBinding>();
                }

                binding.entityId = entry.entity_id;
                binding.assetId = entry.asset_id;
                binding.worldEntityName = entry.world_entity_name;
                binding.role = entry.role;

                var renderer = go.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(go);

                Sprite sprite;
                string reason;
                if (GdaiImportedAssetRegistry.TryGetSpriteForEntity(entry.entity_id, out sprite, out reason))
                {
                    Undo.RecordObject(renderer, "GDAI · Assign preview sprite");
                    renderer.sprite = sprite;
                    applied++;
                }
                else
                {
                    unresolved.Add((entry.world_entity_name ?? entry.entity_id) + " → " + reason);
                }

                go.transform.localPosition = new Vector3(i * Spacing, 0f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log($"[GDAI][Assets][Binding] Applied {applied} sprite preview(s), {unresolved.Count} unresolved." +
                      (unresolved.Count > 0 ? "\n  unresolved: " + string.Join(" | ", unresolved) : ""));

            return $"Registry entries: {entries.Count}\nSprites applied: {applied}\nUnresolved: {unresolved.Count}" +
                   (unresolved.Count > 0 ? "\n\n" + string.Join("\n", unresolved) : "") +
                   "\n\nObjects live under '" + PreviewRootName + "' (preview only — gameplay objects untouched). " +
                   "Scene is marked dirty but not saved.";
        }

        // Display-name sanitize for the preview object label only (matching always uses ids).
        private static string SafeName(string name, string entityId)
        {
            string cleaned = Regex.Replace(name ?? string.Empty, @"[^A-Za-z0-9_]+", "_").Trim('_');
            if (string.IsNullOrEmpty(cleaned))
                cleaned = "entity_" + (entityId ?? "unknown").Replace("-", "").Substring(0, 8);
            return "GDAI_Sprite_" + cleaned;
        }
    }
}
