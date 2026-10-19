using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// =====================================================================================
// GDAI Unity Plugin · Layer A · pre-import validation backstop.
// Pure logic, NO Unity APIs — so it can be reasoned about / unit-tested in isolation.
// Refuses unsafe paths and duplicate top-level generated types BEFORE any file is written.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public enum BundleStatus
    {
        Fetched,
        Validated,
        Imported,
        RefreshTriggered,
        CompilePendingInUnity,
        FailedValidation,
        FailedWrite
    }

    public class ValidationResult
    {
        public bool Ok = true;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public void Fail(string message) { Ok = false; Errors.Add(message); }
        public void Warn(string message) { Warnings.Add(message); }
    }

    public static class CoherentBundleValidator
    {
        public const string GeneratedRoot = "Assets/GDAI_Generated/";

        private static readonly string[] RequiredFiles =
        {
            "ProjectSharedTypes.cs",
            "GameIntegrationController.cs",
            "GDAI_WiringManifest.json",
            "README_WIRING.md"
        };

        // Top-level type declaration matcher (small heuristic guard, not a full C# parser).
        private static readonly Regex TypeDecl = new Regex(
            @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*(?:enum|class|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Multiline);

        public static ValidationResult Validate(GdaiHotReloadSnapshot snap)
        {
            var r = new ValidationResult();

            if (snap == null) { r.Fail("Snapshot is null."); return r; }
            if (snap.assets == null || snap.assets.Count == 0) { r.Fail("Snapshot has no assets."); return r; }

            // --- 1. Coherent-bundle metadata gate ---
            var ctx = snap.context_snapshot;
            if (ctx == null)
            {
                r.Fail("Missing context_snapshot (cannot confirm this is a codegen-assembly coherent bundle).");
            }
            else
            {
                if (ctx.source != "codegen-assembly")
                    r.Fail($"context_snapshot.source must be 'codegen-assembly' (got '{ctx.source ?? "null"}').");
                if (ctx.bundleType != "unity_core_bundle")
                    r.Fail($"context_snapshot.bundleType must be 'unity_core_bundle' (got '{ctx.bundleType ?? "null"}').");
                if (!ctx.compileReadySharedTypes)
                    r.Fail("context_snapshot.compileReadySharedTypes is false — bundle is not compile-ready.");

                string icStatus = ctx.integrationController != null ? ctx.integrationController.status : null;
                if (icStatus != "generated" && icStatus != "partial")
                    r.Fail($"integrationController.status must be 'generated' or 'partial' (got '{icStatus ?? "null"}').");

                // Project-SLASH runtime-sync invariants are surfaced as warnings (not all bundles set them).
                if (!ctx.runtimeReadyDashSync)
                    r.Warn("runtimeReadyDashSync is false — dash runtime sync may not be wired for this bundle.");
                string dashStatus = ctx.dashRuntimeSync != null ? ctx.dashRuntimeSync.status : null;
                if (dashStatus != "patched")
                    r.Warn($"dashRuntimeSync.status is '{dashStatus ?? "null"}' (expected 'patched' for Project-SLASH).");
            }

            if (snap.assets.Count < 7)
                r.Fail($"Asset count {snap.assets.Count} is below the minimum coherent-bundle size (7).");

            // --- 2. Path safety + duplicate path check ---
            var seenPaths = new HashSet<string>();
            foreach (var a in snap.assets)
            {
                if (a == null || string.IsNullOrEmpty(a.path)) { r.Fail("Found an asset with an empty path."); continue; }
                string p = a.path.Replace('\\', '/');

                if (!p.StartsWith(GeneratedRoot))
                    r.Fail($"Path is outside {GeneratedRoot}: '{p}'");
                if (p.Contains(".."))
                    r.Fail($"Path contains '..': '{p}'");
                if (Path.IsPathRooted(p))
                    r.Fail($"Absolute paths are rejected: '{p}'");
                if (RejectsReservedRoot(p))
                    r.Fail($"Path targets a reserved location: '{p}'");
                if (!seenPaths.Add(p))
                    r.Fail($"Duplicate asset path: '{p}'");
            }

            // --- 3. Required files present ---
            foreach (var req in RequiredFiles)
            {
                if (!snap.assets.Exists(a => a.path != null && a.path.Replace('\\', '/').EndsWith("/" + req)))
                    r.Fail($"Missing required coherent-bundle asset: {req}");
            }

            // --- 4. Duplicate top-level type guard (the old DashPhase contamination backstop) ---
            var dups = ScanDuplicateTopLevelTypes(snap.assets);
            foreach (var kv in dups)
                r.Fail($"Duplicate top-level type '{kv.Key}' declared in: {string.Join(", ", kv.Value)}. Refusing import.");

            return r;
        }

        private static bool RejectsReservedRoot(string p)
        {
            // Even under Assets/, never let a bundle write into editor/engine-sensitive trees.
            return p.StartsWith("Assets/Editor/")
                || p.StartsWith("Packages/")
                || p.StartsWith("ProjectSettings/")
                || p.StartsWith("UserSettings/")
                || p.StartsWith("Library/");
        }

        public static Dictionary<string, List<string>> ScanDuplicateTopLevelTypes(List<GdaiSnapshotAsset> assets)
        {
            var typeToFiles = new Dictionary<string, List<string>>();

            foreach (var a in assets)
            {
                if (a == null || a.path == null || a.content == null) continue;
                if (!a.path.EndsWith(".cs")) continue;

                string file = Path.GetFileName(a.path);
                foreach (Match m in TypeDecl.Matches(a.content))
                {
                    string name = m.Groups[1].Value;
                    if (!typeToFiles.TryGetValue(name, out var files))
                    {
                        files = new List<string>();
                        typeToFiles[name] = files;
                    }
                    if (!files.Contains(file)) files.Add(file);
                }
            }

            var dups = new Dictionary<string, List<string>>();
            foreach (var kv in typeToFiles)
                if (kv.Value.Count > 1) dups[kv.Key] = kv.Value;
            return dups;
        }
    }
}
