using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer B2 · auto-bind InputManager's InputActionReference + Camera.
// BINDS EXISTING assets only. Never creates InputActionAsset / InputActionReference /
// GameObject / Component, never edits ProjectSettings, never saves the scene.
// Reflection-only: no compile-time dependency on the generated InputManager class AND no
// compile-time dependency on com.unity.inputsystem (InputActionReference is discovered as a
// UnityEngine.Object and inspected via reflection), so this Editor code compiles regardless
// of whether a bundle is imported or the Input System package is present.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiInputActionReferenceBinder
    {
        private const string InputManagerTypeName = "InputManager";
        private const string InputActionReferenceTypeName = "InputActionReference";

        public sealed class InputBindReport
        {
            public string status = "failed"; // success | partial | failed
            public int boundCount;
            public int totalFields = 4;
            public readonly List<string> boundLines = new List<string>();
            public readonly List<string> alreadyAssigned = new List<string>();
            public readonly List<string> missing = new List<string>();
            public readonly List<string> ambiguous = new List<string>();
            public readonly List<string> warnings = new List<string>();

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Status: {status} · bound {boundCount}/{totalFields} InputManager references.");
                foreach (var b in boundLines) sb.AppendLine("  ✓ " + b);
                foreach (var a in alreadyAssigned) sb.AppendLine("  = " + a);
                if (missing.Count > 0) { sb.AppendLine("Missing:"); foreach (var m in missing) sb.AppendLine("  • " + m); }
                if (ambiguous.Count > 0) { sb.AppendLine("Ambiguous (left unbound — assign manually):"); foreach (var a in ambiguous) sb.AppendLine("  • " + a); }
                foreach (var w in warnings) sb.AppendLine(w);
                if (boundCount > 0) sb.AppendLine("Scene marked dirty. Review InputManager, then save the scene.");
                return sb.ToString().TrimEnd();
            }
        }

        [MenuItem("GDAI/Layer B2 · Auto-bind Input Actions")]
        public static void AutoBindMenu()
        {
            var r = AutoBindInputActions();
            EditorUtility.DisplayDialog("GDAI · Layer B2 Auto-bind Input Actions", r.Summary(), "OK");
        }

        public static InputBindReport AutoBindInputActions()
        {
            var report = new InputBindReport();

            Type imType = FindMonoBehaviourType(InputManagerTypeName);
            if (imType == null)
            {
                report.warnings.Add("InputManager type not found. Import a coherent bundle first, then run Auto-bind Input Actions.");
                Log(report); return report;
            }

            var ims = FindSceneComponents(imType);
            if (ims.Count == 0)
            {
                report.warnings.Add("No InputManager in the open scene. Open the fixture scene first.");
                Log(report); return report;
            }
            Component im = ims.Count == 1 ? ims[0] : ims.FirstOrDefault(c => c.gameObject.name == "InputManager");
            if (im == null)
            {
                report.warnings.Add($"Multiple InputManagers ({ims.Count}) and none named exactly 'InputManager'. Bind manually.");
                Log(report); return report;
            }

            var refs = DiscoverInputActionReferences();
            if (refs.Count == 0)
                report.warnings.Add("No existing InputActionReference assets found. Layer B2 binds existing assets only — nothing was created.");

            Undo.RecordObject(im, "GDAI Auto-bind Input Actions");
            var so = new SerializedObject(im);

            BindActionRef(so, "_pointerPositionRef", refs, ScorePointer, report);
            BindActionRef(so, "_leftClickRef", refs, ScoreLeftClick, report);
            BindActionRef(so, "_rightClickRef", refs, ScoreRightClick, report);
            BindCamera(so, "_mainCamera", report);

            // Only dirty the scene when we actually wrote at least one reference. Runs that only
            // skip already-assigned fields or report missing/ambiguous must not pollute dirty state.
            bool changed = report.boundCount > 0;
            so.ApplyModifiedProperties();
            if (changed)
            {
                EditorUtility.SetDirty(im);
                EditorSceneManager.MarkSceneDirty(im.gameObject.scene);
            }

            int satisfied = report.boundCount + report.alreadyAssigned.Count;
            report.status = satisfied >= report.totalFields ? "success" : (satisfied > 0 ? "partial" : "failed");
            Log(report);
            return report;
        }

        // ---------------- field binding ----------------

        private static void BindActionRef(SerializedObject so, string field, List<UnityEngine.Object> refs,
            Func<UnityEngine.Object, int> score, InputBindReport r)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { r.missing.Add($"{field}: no serialized property on InputManager"); return; }
            if (prop.propertyType != SerializedPropertyType.ObjectReference) { r.missing.Add($"{field}: not an object-reference field"); return; }
            if (prop.objectReferenceValue != null) { r.alreadyAssigned.Add($"{field} already assigned → unchanged"); return; }
            if (refs.Count == 0) { r.missing.Add($"{field}: no InputActionReference assets available"); return; }

            var scored = refs.Select(o => new { o, s = score(o) })
                             .Where(x => x.s > 0)
                             .OrderByDescending(x => x.s)
                             .ToList();
            if (scored.Count == 0) { r.missing.Add($"{field}: no matching InputActionReference"); return; }

            var top = scored[0];
            if (top.s < 2) { r.ambiguous.Add($"{field}: best candidate too weak ({RefLabel(top.o)})"); return; }
            if (scored.Count > 1 && scored[1].s >= top.s) // tie at the top -> unsafe
            {
                r.ambiguous.Add($"{field}: candidates too close: {RefLabel(top.o)} / {RefLabel(scored[1].o)}");
                return;
            }

            prop.objectReferenceValue = top.o; // objectReferenceValue accepts any UnityEngine.Object
            r.boundLines.Add($"{field} -> {RefLabel(top.o)}");
            r.boundCount++;
        }

        private static void BindCamera(SerializedObject so, string field, InputBindReport r)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { r.missing.Add($"{field}: no serialized property on InputManager"); return; }
            if (prop.propertyType != SerializedPropertyType.ObjectReference) { r.missing.Add($"{field}: not an object-reference field"); return; }
            if (prop.objectReferenceValue != null) { r.alreadyAssigned.Add($"{field} already assigned → unchanged"); return; }

            Camera cam = Camera.main;
            if (cam == null)
            {
                var cams = FindSceneComponents(typeof(Camera)).Cast<Camera>().ToList();
                var named = cams.Where(c => c.gameObject.name == "Main Camera").ToList();
                if (named.Count == 1) cam = named[0];
                else if (cams.Count == 1) cam = cams[0];
                else { r.ambiguous.Add(cams.Count == 0 ? $"{field}: no Camera in scene" : $"{field}: multiple cameras, none named 'Main Camera'"); return; }
            }

            prop.objectReferenceValue = cam;
            r.boundLines.Add($"{field} -> {PathOf(cam)}");
            r.boundCount++;
        }

        // ---------------- InputActionReference discovery + scoring (reflection) ----------------

        private static List<UnityEngine.Object> DiscoverInputActionReferences()
        {
            var list = new List<UnityEngine.Object>();
            CollectByFilter("t:" + InputActionReferenceTypeName, list);
            CollectByFilter("t:InputActionAsset", list); // sub-asset InputActionReferences live inside the .inputactions asset
            return list;
        }

        private static void CollectByFilter(string filter, List<UnityEngine.Object> into)
        {
            foreach (var guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset != null && asset.GetType().Name == InputActionReferenceTypeName && !into.Contains(asset))
                        into.Add(asset);
                }
            }
        }

        private static int ScorePointer(UnityEngine.Object refObj)
        {
            var (name, ect, paths) = Describe(refObj);
            int s = 0;
            if (ContainsAny(name, "pointerposition", "pointer", "point", "cursor", "position", "mouseposition", "look", "aim")) s += 2;
            if (string.Equals(ect, "Vector2", StringComparison.OrdinalIgnoreCase)) s += 2;
            if (paths.Any(p => Has(p, "/position"))) s += 2;
            // pointer is a value/axis, not a button press
            if (paths.Any(p => Has(p, "leftbutton") || Has(p, "rightbutton") || Has(p, "/press"))) s -= 2;
            return s;
        }

        private static int ScoreLeftClick(UnityEngine.Object refObj)
        {
            var (name, _, paths) = Describe(refObj);
            if (ContainsAny(name, "right", "secondary", "dash", "altfire")) return -5; // exclude
            if (paths.Any(p => Has(p, "rightbutton"))) return -5;
            int s = 0;
            if (ContainsAny(name, "leftclick", "click", "primary", "select", "fire", "move")) s += 2;
            if (paths.Any(p => Has(p, "leftbutton") || Has(p, "/press"))) s += 2;
            return s;
        }

        private static int ScoreRightClick(UnityEngine.Object refObj)
        {
            var (name, _, paths) = Describe(refObj);
            int s = 0;
            if (ContainsAny(name, "rightclick", "right", "secondary", "dash", "altfire")) s += 2;
            if (paths.Any(p => Has(p, "rightbutton"))) s += 3;
            if (paths.Any(p => Has(p, "leftbutton"))) s -= 2;
            return s;
        }

        // Reflect InputActionReference -> (action.name, expectedControlType, binding paths). No compile-time InputSystem ref.
        private static (string name, string expectedControlType, List<string> paths) Describe(UnityEngine.Object refObj)
        {
            string name = refObj != null ? refObj.name : "";
            string ect = null;
            var paths = new List<string>();
            try
            {
                object action = refObj?.GetType().GetProperty("action")?.GetValue(refObj);
                if (action != null)
                {
                    var at = action.GetType();
                    name = at.GetProperty("name")?.GetValue(action) as string ?? name;
                    ect = at.GetProperty("expectedControlType")?.GetValue(action) as string;
                    if (at.GetProperty("bindings")?.GetValue(action) is IEnumerable bindings)
                    {
                        foreach (var b in bindings)
                        {
                            if (b == null) continue;
                            var bt = b.GetType();
                            string ep = bt.GetProperty("effectivePath")?.GetValue(b) as string
                                        ?? bt.GetProperty("path")?.GetValue(b) as string;
                            if (!string.IsNullOrEmpty(ep)) paths.Add(ep);
                        }
                    }
                }
            }
            catch { /* reflection best-effort; fall back to asset name only */ }
            return (name ?? "", ect, paths);
        }

        private static string RefLabel(UnityEngine.Object refObj)
        {
            var (name, _, _) = Describe(refObj);
            return string.IsNullOrEmpty(name) ? (refObj != null ? refObj.name : "(null)") : name;
        }

        // ---------------- shared scene utilities (self-contained; do not touch Layer B) ----------------

        private static Type FindMonoBehaviourType(string typeName)
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (t.Name == typeName) return t;
            return null;
        }

        private static List<Component> FindSceneComponents(Type t)
        {
            // Delegates to the shared Unity-6-safe query (Resources.FindObjectsOfTypeAll + filter).
            return GdaiLayerBSceneQuery.FindSceneComponents(t);
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            string h = haystack.ToLowerInvariant();
            return needles.Any(n => h.Contains(n));
        }

        private static bool Has(string s, string sub) => !string.IsNullOrEmpty(s) && s.ToLowerInvariant().Contains(sub);

        private static string PathOf(UnityEngine.Object o)
        {
            Transform t = o as Transform ?? (o as Component)?.transform;
            if (t == null) return o ? o.name : "(null)";
            var stack = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Add(cur.name);
            stack.Reverse();
            return "/" + string.Join("/", stack);
        }

        private static void Log(InputBindReport r)
        {
            foreach (var b in r.boundLines) Debug.Log($"[GDAI][LayerB2] Bound {b}");
            foreach (var a in r.alreadyAssigned) Debug.Log($"[GDAI][LayerB2] {a}");
            foreach (var m in r.missing) Debug.LogWarning($"[GDAI][LayerB2] Missing {m}");
            foreach (var a in r.ambiguous) Debug.LogWarning($"[GDAI][LayerB2] PARTIAL: {a}");
            foreach (var w in r.warnings) Debug.LogWarning($"[GDAI][LayerB2] {w}");
        }
    }
}
