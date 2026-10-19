using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

// =====================================================================================
// GDAI Unity Plugin · MVP-C · device-pairing client (POST /functions/v1/unity-plugin-connection).
// RFC 8628-style: start → user approves in browser → poll with device_code + poll_secret →
// receive a scoped gdai_plugin_v1.* token exactly once. No Supabase anon/service key used.
// device_code / poll_secret are secrets held only by the plugin and never surfaced in UI.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public static class GdaiPluginConnectionClient
    {
        public static async Task<GdaiConnectionStartResponse> Start(string functionsBase, string clientName, string pluginVersion)
        {
            string url = ConnectionUrl(functionsBase);
            string body = JsonConvert.SerializeObject(new { action = "start", client_name = clientName, plugin_version = pluginVersion });
            string json = await PostJson(url, body, null);
            var resp = JsonConvert.DeserializeObject<GdaiConnectionStartResponse>(json);
            if (resp == null || string.IsNullOrEmpty(resp.device_code) || string.IsNullOrEmpty(resp.poll_secret))
                throw new GdaiBundleProxyException("Connection start failed: invalid response.");
            return resp;
        }

        public static async Task<GdaiConnectionPollResponse> Poll(string functionsBase, string deviceCode, string pollSecret)
        {
            string url = ConnectionUrl(functionsBase);
            string body = JsonConvert.SerializeObject(new { action = "poll", device_code = deviceCode, poll_secret = pollSecret });
            string json = await PostJson(url, body, null);
            var resp = JsonConvert.DeserializeObject<GdaiConnectionPollResponse>(json);
            if (resp == null) throw new GdaiBundleProxyException("Poll failed: invalid response.");
            return resp;
        }

        /// <summary>Best-effort server-side revoke. Local Disconnect already clears the token regardless.</summary>
        public static async Task Revoke(string functionsBase, string pluginToken)
        {
            try
            {
                string url = ConnectionUrl(functionsBase);
                string body = JsonConvert.SerializeObject(new { action = "revoke" });
                await PostJson(url, body, pluginToken);
            }
            catch { /* revoke is best-effort; ignore server errors */ }
        }

        private static string ConnectionUrl(string functionsBase) =>
            functionsBase.TrimEnd('/') + "/unity-plugin-connection";

        private static async Task<string> PostJson(string url, string body, string bearerToken)
        {
            using (var req = new UnityWebRequest(url, "POST"))
            {
                byte[] payload = Encoding.UTF8.GetBytes(body ?? "{}");
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(bearerToken))
                    req.SetRequestHeader("Authorization", "Bearer " + bearerToken);

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                long code = req.responseCode;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    switch (code)
                    {
                        case 401: throw new GdaiBundleProxyException("Connection auth failed (401). The code/secret is invalid or expired.");
                        case 404: throw new GdaiBundleProxyException("Connection endpoint not found (404).");
                        case 429: throw new GdaiBundleProxyException("Polling too fast (429). Respect the interval and retry.");
                        default:
                            if (code >= 500 || code == 0) throw new GdaiBundleProxyException("Connection service unavailable. Retry shortly.");
                            throw new GdaiBundleProxyException($"Connection request failed (HTTP {code}).");
                    }
                }
                return req.downloadHandler.text;
            }
        }
    }
}
