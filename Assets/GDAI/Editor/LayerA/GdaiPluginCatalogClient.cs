using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

// =====================================================================================
// GDAI Unity Plugin · MVP-C · catalog client (unity-plugin-projects / unity-plugin-bundles).
// Authenticated ONLY with the scoped gdai_plugin_v1.* token (Authorization: Bearer).
// Returns metadata only; bundle file contents are fetched later via unity-bundle-proxy.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public static class GdaiPluginCatalogClient
    {
        public static async Task<List<GdaiCatalogProject>> ListProjects(string functionsBase, string pluginToken)
        {
            string url = functionsBase.TrimEnd('/') + "/unity-plugin-projects";
            string json = await Get(url, pluginToken);
            var resp = JsonConvert.DeserializeObject<GdaiCatalogProjectsResponse>(json);
            return resp != null && resp.projects != null ? resp.projects : new List<GdaiCatalogProject>();
        }

        public static async Task<List<GdaiCatalogBundle>> ListBundles(string functionsBase, string pluginToken, string projectId, int limit)
        {
            string url = functionsBase.TrimEnd('/') + "/unity-plugin-bundles" +
                         $"?project_id={UnityWebRequest.EscapeURL(projectId)}&limit={limit}";
            string json = await Get(url, pluginToken);
            var resp = JsonConvert.DeserializeObject<GdaiCatalogBundlesResponse>(json);
            return resp != null && resp.bundles != null ? resp.bundles : new List<GdaiCatalogBundle>();
        }

        private static async Task<string> Get(string url, string pluginToken)
        {
            if (string.IsNullOrEmpty(pluginToken))
                throw new GdaiBundleProxyException("Not connected. Connect to GDAI first.");

            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", "Bearer " + pluginToken);
                req.SetRequestHeader("Accept", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                long code = req.responseCode;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    switch (code)
                    {
                        case 401: throw new GdaiBundleProxyException("Session expired (401). Reconnect to GDAI.");
                        case 403: throw new GdaiBundleProxyException("No access to this project (403).");
                        case 404: throw new GdaiBundleProxyException("Catalog endpoint not found (404).");
                        default:
                            if (code >= 500 || code == 0) throw new GdaiBundleProxyException("Catalog service unavailable. Retry shortly.");
                            throw new GdaiBundleProxyException($"Catalog request failed (HTTP {code}).");
                    }
                }
                return req.downloadHandler.text;
            }
        }
    }
}
