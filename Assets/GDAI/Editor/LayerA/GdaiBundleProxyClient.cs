using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

// =====================================================================================
// GDAI Unity Plugin · Production Bundle Proxy client.
// Talks ONLY to the backend `unity-bundle-proxy` Edge Function with a user JWT
// (Authorization: Bearer). NEVER uses apikey / anon key / service_role, and NEVER
// touches /rest/v1/hot_reload_snapshots. Returns a normalized DTO, then adapts it to
// the existing GdaiHotReloadSnapshot so the SAME Layer A validate + import core runs
// (backup outside Assets, .meta GUID preservation — unchanged).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    /// <summary>Thrown with a user-readable message mapped from the proxy HTTP status.</summary>
    public class GdaiBundleProxyException : Exception
    {
        public GdaiBundleProxyException(string message) : base(message) { }
    }

    public static class GdaiBundleProxyClient
    {
        // ---- fetch: latest coherent bundle for a project ----
        public static Task<GdaiBundleProxyDto> FetchLatest(string functionUrl, string projectId, string userJwt)
        {
            string url = $"{functionUrl.TrimEnd('/')}?project_id={UnityWebRequest.EscapeURL(projectId)}&latest=1";
            return Get(url, userJwt);
        }

        // ---- fetch: specific snapshot ----
        public static Task<GdaiBundleProxyDto> FetchBySnapshot(string functionUrl, string projectId, string snapshotId, string userJwt)
        {
            string url = $"{functionUrl.TrimEnd('/')}?project_id={UnityWebRequest.EscapeURL(projectId)}" +
                         $"&snapshot_id={UnityWebRequest.EscapeURL(snapshotId)}";
            return Get(url, userJwt);
        }

        private static async Task<GdaiBundleProxyDto> Get(string url, string userJwt)
        {
            if (string.IsNullOrWhiteSpace(userJwt))
                throw new GdaiBundleProxyException("A user access token is required. Paste your GDAI web session token.");

            using (var req = UnityWebRequest.Get(url))
            {
                // Production auth: ONLY the user JWT. No apikey, no anon key, no service_role.
                req.SetRequestHeader("Authorization", "Bearer " + userJwt);
                req.SetRequestHeader("Accept", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                long code = req.responseCode;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Map status codes to user-readable, non-leaky messages.
                    switch (code)
                    {
                        case 401: throw new GdaiBundleProxyException("Authentication failed (401). Refresh/login and paste a valid user access token.");
                        case 403: throw new GdaiBundleProxyException("No access to this project (403).");
                        case 404: throw new GdaiBundleProxyException("No coherent Unity bundle found / snapshot not found (404).");
                        case 409: throw new GdaiBundleProxyException("Snapshot is not a coherent Unity bundle (409).");
                        default:
                            if (code >= 500 || code == 0)
                                throw new GdaiBundleProxyException("Bundle proxy unavailable. Check backend status or retry.");
                            throw new GdaiBundleProxyException($"Bundle proxy request failed (HTTP {code}).");
                    }
                }

                GdaiBundleProxyDto dto;
                try { dto = JsonConvert.DeserializeObject<GdaiBundleProxyDto>(req.downloadHandler.text); }
                catch { throw new GdaiBundleProxyException("Invalid bundle proxy response."); }

                if (dto == null) throw new GdaiBundleProxyException("Invalid bundle proxy response.");
                return dto;
            }
        }

        /// <summary>
        /// Production-side validation of the DTO (shape + path safety + per-file SHA-256),
        /// returning the list of errors. Empty list = OK. Does NOT write anything.
        /// </summary>
        public static List<string> ValidateDto(GdaiBundleProxyDto dto)
        {
            var errors = new List<string>();
            if (dto == null) { errors.Add("Null DTO."); return errors; }

            if (dto.target_engine != "unity")
                errors.Add($"target_engine must be 'unity' (got '{dto.target_engine ?? "null"}').");
            if (dto.bundle_type != "unity_core_bundle")
                errors.Add($"bundle_type must be 'unity_core_bundle' (got '{dto.bundle_type ?? "null"}').");
            if (dto.files == null || dto.files.Count == 0)
                errors.Add("Bundle has no files.");

            string prefix = CoherentBundleImporter.GeneratedFolder + "/"; // "Assets/GDAI_Generated/"
            var seen = new HashSet<string>();
            if (dto.files != null)
            {
                foreach (var f in dto.files)
                {
                    if (f == null || string.IsNullOrEmpty(f.path)) { errors.Add("A file has an empty path."); continue; }
                    string p = f.path.Replace('\\', '/');
                    if (!p.StartsWith(prefix, StringComparison.Ordinal)) errors.Add($"Path outside {prefix}: {p}");
                    if (p.Contains("..")) errors.Add($"Path contains '..': {p}");
                    if (System.IO.Path.IsPathRooted(p)) errors.Add($"Absolute path rejected: {p}");
                    if (!seen.Add(p)) errors.Add($"Duplicate path: {p}");
                    if (f.content == null) errors.Add($"File has null content: {p}");
                    if (string.IsNullOrEmpty(f.sha256)) errors.Add($"File missing sha256: {p}");
                    else if (!GdaiHashUtility.Matches(f.content, f.sha256))
                        errors.Add($"SHA-256 mismatch for {p} (content does not match declared hash).");
                }
            }
            return errors;
        }

        /// <summary>
        /// Adapts the normalized DTO into the existing GdaiHotReloadSnapshot so the SAME
        /// CoherentBundleValidator + CoherentBundleImporter.ImportVerbatim path runs.
        /// The proxy only serves coherent codegen-assembly unity bundles, so source is set
        /// accordingly purely to satisfy the existing validator (which was written for raw rows).
        /// </summary>
        public static GdaiHotReloadSnapshot ToSnapshot(GdaiBundleProxyDto dto)
        {
            var snap = new GdaiHotReloadSnapshot
            {
                id = dto.snapshot_id,
                project_id = dto.project_id,
                target_engine = dto.target_engine,
                created_at = dto.created_at,
                assets = new List<GdaiSnapshotAsset>(),
                context_snapshot = new GdaiContextSnapshot
                {
                    source = "codegen-assembly", // proxy guarantees coherent codegen-assembly unity bundle
                    bundleType = dto.bundle_type,
                    compileReadySharedTypes = dto.metadata?.compileReadySharedTypes == true, // C2: null/false/absent → false
                    runtimeReadyDashSync = dto.metadata?.runtimeReadyDashSync == true,
                    integrationController = new GdaiIntegrationController
                    {
                        status = dto.metadata != null && dto.metadata.integrationController != null
                            ? dto.metadata.integrationController.status
                            : null
                    }
                }
            };
            if (dto.files != null)
            {
                foreach (var f in dto.files)
                    snap.assets.Add(new GdaiSnapshotAsset { path = f.path, content = f.content, type = f.type });
            }
            return snap;
        }
    }
}
