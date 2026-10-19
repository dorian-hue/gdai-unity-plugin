using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// UNITY-SCENE-VISUAL-CLEANUP-1E · D · Imported sprite alpha / magenta diagnosis (Editor, Layer B).
//
// DIAGNOSTIC ONLY — locates whether a big magenta / opaque background comes from the SOURCE PNG
// (asset QA problem) vs a clean-alpha asset. Reads the original PNG bytes under Art/ and samples
// its corners. Does NOT auto-cut-out, does NOT change import settings, does NOT touch the scene.
// If a source PNG carries an opaque magenta background it prints VISUAL_ASSET_ALPHA_SOURCE_PROBLEM
// and the fix is deferred to the next-stage Asset QA / generation cleanup (not this task).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiImportedSpriteAlphaValidator
    {
        public const string ArtFolder = "Assets/GDAI_Generated/Art";

        [MenuItem("GDAI/Assets · Validate Imported Sprite Alpha")]
        public static void ValidateMenu()
        {
            string report = Validate();
            Debug.Log("[GDAI][Assets][SpriteAlpha] " + report);
            EditorUtility.DisplayDialog("GDAI · Validate Imported Sprite Alpha", report, "OK");
        }

        public static string Validate()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                return "Art folder missing (" + ArtFolder + ") — import a bundle first.";

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var lines = new List<string>();
            int magenta = 0, alphaOk = 0, opaqueOther = 0, unreadable = 0, total = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                total++;

                bool alphaIsTransparency = false;
                if (AssetImporter.GetAtPath(path) is TextureImporter ti) alphaIsTransparency = ti.alphaIsTransparency;

                string verdict;
                try
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(projectRoot, path));
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes))   // decodes PNG/JPG; webp may fail → unreadable
                    {
                        Object.DestroyImmediate(tex);
                        verdict = "unreadable_source"; unreadable++;
                    }
                    else
                    {
                        verdict = ClassifyCorners(tex);
                        Object.DestroyImmediate(tex);
                        if (verdict == "MAGENTA_SOURCE") magenta++;
                        else if (verdict == "alpha_ok") alphaOk++;
                        else opaqueOther++;
                    }
                }
                catch (System.Exception e) { verdict = "error:" + e.Message; unreadable++; }

                lines.Add("  · " + Path.GetFileName(path) + " → " + verdict + " (alphaIsTransparency=" + alphaIsTransparency + ")");
            }

            var sb = new StringBuilder();
            sb.AppendLine(magenta > 0 ? "VISUAL_ASSET_ALPHA_SOURCE_PROBLEM" : "[GDAI] Imported sprite alpha check");
            sb.AppendLine("Sprites: " + total + " · magenta-source: " + magenta + " · clean-alpha: " + alphaOk +
                          " · opaque-non-magenta: " + opaqueOther + " · unreadable: " + unreadable);
            if (lines.Count > 0) sb.AppendLine(string.Join("\n", lines));
            if (magenta > 0)
                sb.AppendLine("\n→ The magenta comes from the SOURCE PNG (not a Unity import bug). " +
                              "Defer to Asset QA / generation cleanup — do NOT auto-cut-out in this task.");
            return sb.ToString().TrimEnd();
        }

        // Opaque magenta corners → source has a magenta background. Fully transparent corners → alpha ok.
        private static string ClassifyCorners(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            if (w < 2 || h < 2) return "too_small";
            Color[] corners =
            {
                tex.GetPixel(0, 0), tex.GetPixel(w - 1, 0), tex.GetPixel(0, h - 1), tex.GetPixel(w - 1, h - 1),
            };
            int magentaCorners = 0, transparentCorners = 0;
            foreach (var c in corners)
            {
                if (IsMagenta(c)) magentaCorners++;
                if (c.a < 0.1f) transparentCorners++;
            }
            if (magentaCorners >= 2) return "MAGENTA_SOURCE";
            if (transparentCorners >= 3) return "alpha_ok";
            return "opaque_non_magenta";
        }

        private static bool IsMagenta(Color c)
        {
            return c.a > 0.9f && c.r > 0.8f && c.g < 0.25f && c.b > 0.8f;
        }
    }
}
