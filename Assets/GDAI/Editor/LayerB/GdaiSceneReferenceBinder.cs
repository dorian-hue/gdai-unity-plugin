using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer B · auto-bind GameIntegrationController scene references.
// BINDS EXISTING scene objects only. Never creates GameObjects / scenes / prefabs /
// InputAction assets, never edits ProjectSettings/Tags/Layers, never modifies generated
// C#. Uses reflection (TypeCache) + SerializedObject by field name, so this Editor code
// compiles whether or not a generated bundle has been imported yet.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public class GdaiSceneReferenceBinder
    {
        private const string GicTypeName = "GameIntegrationController";

        // GIC serialized field names (deterministic-manifest-template). If these differ,
        // the corresponding property is reported missing and that field is skipped.
        private static readonly string[] Fields =
            { "inputManager", "characterStateMachine", "combatLocatorSystem", "enemyDirector", "player" };

        public class BindReport
        {
            public string status = "failed"; // success | partial | failed
            public int boundCount;
            public int totalFields = 5;
            public readonly List<string> boundLines = new List<string>();
            public readonly List<string> missing = new List<string>();
            public readonly List<string> ambiguous = new List<string>();
            public readonly List<string> warnings = new List<string>();

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Status: {status} · bound {boundCount}/{totalFields} references.");
                foreach (var b in boundLines) sb.AppendLine("  ✓ " + b);
                if (missing.Count > 0) { sb.AppendLine("Missing:"); foreach (var m in missing) sb.AppendLine("  • " + m); }
                if (ambiguous.Count > 0) { sb.AppendLine("Ambiguous (left unbound — assign manually or rename):"); foreach (var a in ambiguous) sb.AppendLine("  • " + a); }
                foreach (var w in warnings) sb.AppendLine(w);
                if (boundCount > 0) sb.AppendLine("Scene marked dirty. Review GameIntegrationController, then save the scene.");
                return sb.ToString().TrimEnd();
            }
        }

        [MenuItem("GDAI/Layer B · Auto-bind Current Scene")]
        public static void AutoBindMenu()
        {
            var r = AutoBind();
            EditorUtility.DisplayDialog("GDAI · Layer B Auto-bind", r.Summary(), "OK");
        }

        public static BindReport AutoBind()
        {
            var report = new BindReport();

            Type gicType = FindMonoBehaviourType(GicTypeName);
            if (gicType == null)
            {
                report.warnings.Add("GameIntegrationController type not found. Import a coherent bundle first, then run Auto-bind.");
                Log(report);
                return report;
            }

            var gics = FindSceneComponents(gicType);
            if (gics.Count == 0)
            {
                report.warnings.Add("No GameIntegrationController in the open scene. Open the fixture scene first.");
                Log(report);
                return report;
            }
            if (gics.Count > 1)
            {
                report.warnings.Add($"Multiple GameIntegrationControllers ({gics.Count}) found. Layer B v1 binds only when exactly one exists.");
                Log(report);
                return report;
            }

            Component gic = gics[0];

            // Resolve runtime types by name (any may be null if the bundle isn't imported).
            Type tInput = FindMonoBehaviourType("InputManager");
            Type tCsm = FindMonoBehaviourType("CharacterStateMachine");
            Type tCombat = FindMonoBehaviourType("CombatLocatorSystem");
            Type tEnemy = FindMonoBehaviourType("EnemyDirector");

            // Select candidates (each records missing/ambiguous as needed).
            Component csm = SelectCharacterStateMachine(tCsm, report);
            Component inputManager = SelectSingleOrNamed(tInput, "inputManager", "InputManager", report);
            Component combat = SelectCombatLocator(tCombat, csm, report);
            Transform player = SelectPlayer(csm, report);
            Component enemyDirector = SelectEnemyManager(tEnemy, report);

            // Bind via SerializedObject (no compile-time dependency on generated classes).
            Undo.RecordObject(gic, "GDAI Auto-bind Scene References");
            var so = new SerializedObject(gic);
            BindField(so, "inputManager", inputManager, report);
            BindField(so, "characterStateMachine", csm, report);
            BindField(so, "combatLocatorSystem", combat, report);
            BindField(so, "enemyDirector", enemyDirector, report);
            BindField(so, "player", player, report);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(gic);
            EditorSceneManager.MarkSceneDirty(gic.gameObject.scene);

            report.status = report.boundCount == report.totalFields ? "success"
                           : (report.boundCount > 0 ? "partial" : "failed");
            Log(report);
            return report;
        }

        // ---------------- selection helpers ----------------

        private static Component SelectSingleOrNamed(Type t, string field, string preferredName, BindReport r)
        {
            if (t == null) { r.missing.Add($"{field}: type '{preferredName}' not found (import bundle?)"); return null; }
            var comps = FindSceneComponents(t);
            if (comps.Count == 0) { r.missing.Add($"{field}: no '{preferredName}' component in scene"); return null; }
            if (comps.Count == 1) return comps[0];

            var named = comps.Where(c => c.gameObject.name == preferredName).ToList();
            if (named.Count == 1) return named[0];

            r.ambiguous.Add($"{field}: {Names(comps)}");
            return null;
        }

        private static Component SelectCharacterStateMachine(Type t, BindReport r)
        {
            if (t == null) { r.missing.Add("characterStateMachine: type 'CharacterStateMachine' not found (import bundle?)"); return null; }
            var comps = FindSceneComponents(t);
            if (comps.Count == 0) { r.missing.Add("characterStateMachine: none in scene"); return null; }

            var named = comps.Where(c => c.gameObject.name == "Player").ToList();
            if (named.Count == 1) return named[0];
            var tagged = comps.Where(IsTaggedPlayer).ToList();
            if (tagged.Count == 1) return tagged[0];
            if (comps.Count == 1) return comps[0];

            r.ambiguous.Add($"characterStateMachine: {Names(comps)}");
            return null;
        }

        private static Component SelectCombatLocator(Type t, Component csm, BindReport r)
        {
            if (t == null) { r.missing.Add("combatLocatorSystem: type 'CombatLocatorSystem' not found (import bundle?)"); return null; }

            // Preferred: the CombatLocatorSystem on the SAME GameObject as the selected
            // CharacterStateMachine (the player authority object owns both). This direct
            // GetComponent lookup does not depend on the scene enumeration finding it.
            if (csm != null)
            {
                var sameGo = csm.gameObject.GetComponent(t);
                if (sameGo != null) return sameGo;
            }

            // Fallback: full-scene candidate logic.
            var comps = FindSceneComponents(t);
            if (comps.Count == 0) { r.missing.Add("combatLocatorSystem: none in scene"); return null; }
            var named = comps.Where(c => c.gameObject.name == "Player").ToList();
            if (named.Count == 1) return named[0];
            if (comps.Count == 1) return comps[0];

            r.ambiguous.Add($"combatLocatorSystem: {Names(comps)}");
            return null;
        }

        private static Transform SelectPlayer(Component csm, BindReport r)
        {
            if (csm != null) return csm.transform; // strongest: the player owns the state machine

            var tagged = SceneTransforms().Where(tr => IsTaggedPlayer(tr)).ToList();
            if (tagged.Count == 1) return tagged[0];
            var named = SceneTransforms().Where(tr => tr.gameObject.name == "Player").ToList();
            if (named.Count == 1) return named[0];

            r.ambiguous.Add(tagged.Count + named.Count == 0
                ? "player: no Player-tagged/named Transform found"
                : "player: multiple Player candidates");
            return null;
        }

        private static Component SelectEnemyManager(Type t, BindReport r)
        {
            if (t == null) { r.missing.Add("enemyDirector: type 'EnemyDirector' not found (import bundle?)"); return null; }
            var comps = FindSceneComponents(t);
            if (comps.Count == 0) { r.missing.Add("enemyDirector: none in scene"); return null; }

            // Strongest signal: manager mode = serialized 'enemyPrefab' assigned.
            var managers = comps.Where(HasEnemyPrefabAssigned).ToList();
            if (managers.Count == 1) return managers[0];

            var exact = comps.Where(c => c.gameObject.name == "EnemyManager").ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) { r.ambiguous.Add($"enemyDirector: multiple 'EnemyManager' objects: {Names(exact)}"); return null; }

            var containsMgr = comps.Where(c => c.gameObject.name.Contains("EnemyManager")).ToList();
            if (containsMgr.Count == 1) return containsMgr[0];
            var containsDir = comps.Where(c => c.gameObject.name.Contains("EnemyDirector")).ToList();
            if (containsDir.Count == 1) return containsDir[0];

            // Managers carry no physics; enemy instances have Rigidbody2D/Collider2D.
            var noPhysics = comps.Where(c => !HasPhysics2D(c)).ToList();
            if (noPhysics.Count == 1) return noPhysics[0];

            // Unsafe to guess between manager and enemy instances → leave unbound.
            r.ambiguous.Add($"enemyDirector: {Names(comps)} (could not distinguish manager from enemy instance)");
            return null;
        }

        // ---------------- binding + utilities ----------------

        private static void BindField(SerializedObject so, string field, UnityEngine.Object value, BindReport r)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { r.missing.Add($"{field}: no serialized property on GameIntegrationController"); return; }
            if (value == null) return; // already reported as missing/ambiguous by the selector
            prop.objectReferenceValue = value;
            r.boundLines.Add($"{field} -> {PathOf(value)}");
            r.boundCount++;
        }

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

        private static IEnumerable<Transform> SceneTransforms()
        {
            return FindSceneComponents(typeof(Transform)).Cast<Transform>();
        }

        private static bool IsTaggedPlayer(Component c)
        {
            // "Player" is a Unity built-in tag, always defined → CompareTag is safe.
            return c != null && c.gameObject.CompareTag("Player");
        }

        private static bool HasPhysics2D(Component c)
        {
            return c.GetComponent<Rigidbody2D>() != null || c.GetComponent<Collider2D>() != null;
        }

        private static bool HasEnemyPrefabAssigned(Component enemyDirector)
        {
            var so = new SerializedObject(enemyDirector);
            var prop = so.FindProperty("enemyPrefab");
            return prop != null && prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null;
        }

        private static string Names(IEnumerable<Component> comps)
        {
            return string.Join(", ", comps.Select(c => PathOf(c)));
        }

        private static string PathOf(UnityEngine.Object o)
        {
            Transform t = o as Transform ?? (o as Component)?.transform;
            if (t == null) return o ? o.name : "(null)";
            var stack = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Add(cur.name);
            stack.Reverse();
            return "/" + string.Join("/", stack);
        }

        private static void Log(BindReport r)
        {
            foreach (var b in r.boundLines) Debug.Log($"[GDAI][LayerB] Bound {b}");
            foreach (var m in r.missing) Debug.LogWarning($"[GDAI][LayerB] Missing {m}");
            foreach (var a in r.ambiguous) Debug.LogWarning($"[GDAI][LayerB] PARTIAL: {a}");
            foreach (var w in r.warnings) Debug.LogWarning($"[GDAI][LayerB] {w}");
        }
    }
}
