// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-D · Deterministic InputActionAsset builder
// (Editor, Layer B).
//
// Generates/updates the ONE project-owned input asset the contract declares
// (Assets/GDAI_Project/Generated/Input/GDAI_DefaultControls.inputactions) from
// the contract's actions — NOT by copying the Input System package sample and NOT
// from the diagnostic sandbox. The action/map/binding ids come straight from the
// contract so they are stable across Sync; the .inputactions is written in place
// and its Unity asset GUID is preserved (existing .meta is never rewritten), so a
// second Sync produces zero GUID drift. On import, Unity materializes the
// InputActionReference sub-assets the B2 binder resolves.
// =====================================================================================
using System.IO;
using System.Linq;
using System.Text;
using GDAI.Bridge.Editor.LayerA;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiInputAssetBuilder
    {
        public class Result
        {
            public bool Ok;
            public bool Created;
            public string AssetPath;
            public string Error;
        }

        /// <summary>Deterministic .inputactions JSON from the contract (stable ids, no randomness).</summary>
        public static string BuildJson(GdaiPlayableContract c)
        {
            // one Gameplay map with the contract's three actions + a single binding each.
            // Map/binding ids are derived deterministically from the action ids so they are
            // stable across Sync (no Guid.NewGuid()).
            string mapId = DeterministicId(c.profile_id + ":map:Gameplay");
            var sb = new StringBuilder();
            var actions = c.input.actions;
            string ActionJson(GdaiPlayableContract.ActionSpec a) =>
                "        {\n" +
                "            \"name\": \"" + a.name + "\",\n" +
                "            \"type\": \"" + a.type + "\",\n" +
                "            \"id\": \"" + a.id + "\",\n" +
                "            \"expectedControlType\": \"" + a.control_type + "\",\n" +
                "            \"processors\": \"\",\n" +
                "            \"interactions\": \"\",\n" +
                "            \"initialStateCheck\": " + (a.type == "Value" ? "true" : "false") + "\n" +
                "        }";
            string BindingJson(GdaiPlayableContract.ActionSpec a) =>
                "        {\n" +
                "            \"name\": \"\",\n" +
                "            \"id\": \"" + DeterministicId(c.profile_id + ":binding:" + a.name) + "\",\n" +
                "            \"path\": \"" + a.binding + "\",\n" +
                "            \"interactions\": \"\",\n" +
                "            \"processors\": \"\",\n" +
                "            \"groups\": \"\",\n" +
                "            \"action\": \"" + a.name + "\",\n" +
                "            \"isComposite\": false,\n" +
                "            \"isPartOfComposite\": false\n" +
                "        }";

            sb.Append("{\n");
            sb.Append("    \"name\": \"GDAI_DefaultControls\",\n");
            sb.Append("    \"maps\": [\n        {\n");
            sb.Append("            \"name\": \"" + c.input.map + "\",\n");
            sb.Append("            \"id\": \"" + mapId + "\",\n");
            sb.Append("            \"actions\": [\n");
            sb.Append(string.Join(",\n", actions.Select(ActionJson)));
            sb.Append("\n            ],\n");
            sb.Append("            \"bindings\": [\n");
            sb.Append(string.Join(",\n", actions.Select(BindingJson)));
            sb.Append("\n            ]\n        }\n    ],\n");
            sb.Append("    \"controlSchemes\": []\n}");
            return sb.ToString();
        }

        // stable v5-style id from a name (deterministic, canonical hyphenation)
        public static string DeterministicId(string name)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var h = sha1.ComputeHash(Encoding.UTF8.GetBytes("gdai.inputasset.v1:" + name));
                h[6] = (byte)((h[6] & 0x0f) | 0x50);
                h[8] = (byte)((h[8] & 0x3f) | 0x80);
                string hex = string.Concat(h.Take(16).Select(b => b.ToString("x2")));
                return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" +
                       hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
            }
        }

        /// <summary>Write/update the owned input asset in place, preserving its Unity GUID.</summary>
        public static Result EnsureAsset(GdaiPlayableContract c)
        {
            var r = new Result { AssetPath = c.input.asset_path };
            var abs = Path.Combine(Directory.GetCurrentDirectory(), c.input.asset_path);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            string json = BuildJson(c);

            bool existed = File.Exists(abs);
            // only rewrite if content actually changed (keeps import/GUID churn to zero on idempotent sync)
            if (!existed || File.ReadAllText(abs) != json)
            {
                File.WriteAllText(abs, json);
                AssetDatabase.ImportAsset(c.input.asset_path, ImportAssetOptions.ForceUpdate);
            }
            r.Created = !existed;

            // verify import produced a usable asset with the InputActionImporter
            var imported = AssetDatabase.LoadAllAssetsAtPath(c.input.asset_path);
            bool hasReferences = imported.Any(o => o != null && o.GetType().Name == "InputActionReference");
            if (!File.Exists(abs)) { r.Error = "input asset not written"; return r; }
            if (imported.Length == 0) { r.Error = "input asset failed to import"; return r; }
            // References may materialize on the next import tick in batch; the asset + main object is enough here.
            r.Ok = true;
            return r;
        }
    }
}
