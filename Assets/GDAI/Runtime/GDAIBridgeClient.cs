using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GDAI.Runtime.UI;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace GDAI.Bridge
{
    /// <summary>
    /// Bridge API client using UnityWebRequest coroutine pattern.
    /// </summary>
    public class GDAIBridgeClient
    {
        private readonly string _supabaseUrl;
        private readonly string _anonKey;
        private readonly string _jwtToken;

        public GDAIBridgeClient(string supabaseUrl, string anonKey, string jwtToken)
        {
            _supabaseUrl = supabaseUrl.TrimEnd('/');
            _anonKey = anonKey;
            _jwtToken = jwtToken;
        }

        public IEnumerator FetchArchitectureGraph(
            string projectId,
            Action<ArchitectureGraphResponse> onSuccess,
            Action<string> onError)
        {
            string url = $"{_supabaseUrl}/functions/v1/bridge-api/v1/projects/{projectId}/artifacts?type=architecture_graph";
            yield return FetchArtifact<ArchitectureGraphResponse>(url, "architecture_graph", onSuccess, onError);
        }

        public IEnumerator FetchWorldEntities(
            string projectId,
            Action<WorldEntitiesResponse> onSuccess,
            Action<string> onError)
        {
            string url = $"{_supabaseUrl}/functions/v1/bridge-api/v1/projects/{projectId}/artifacts?type=world_entities";
            yield return FetchArtifact<WorldEntitiesResponse>(url, "world_entities", onSuccess, onError);
        }

        public IEnumerator FetchCodeIntent(
            string projectId,
            Action<CodeIntentResponse> onSuccess,
            Action<string> onError)
        {
            string url = $"{_supabaseUrl}/functions/v1/bridge-api/v1/projects/{projectId}/artifacts?type=code_intent";
            yield return FetchArtifactWithNamespaceWorkaround(url, "code_intent", onSuccess, onError);
        }

        public IEnumerator FetchUIExportPackage(
            string projectId,
            Action<UIExportPackage> onSuccess,
            Action<string> onError)
        {
            string url = $"{_supabaseUrl}/functions/v1/bridge-api/v1/projects/{projectId}/ui-export";
            Debug.Log($"[GDAI] Fetching ui_export from: {url}");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_jwtToken}");
                req.SetRequestHeader("apikey", _anonKey);
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = $"HTTP {req.responseCode}: {req.error}\nBody: {req.downloadHandler.text}";
                    Debug.LogError($"[GDAI] ui_export fetch failed: {err}");
                    onError?.Invoke(err);
                    yield break;
                }

                string json = req.downloadHandler.text;
                try
                {
                    UIExportPackage parsed = JsonConvert.DeserializeObject<UIExportPackage>(json);
                    if (parsed == null)
                    {
                        onError?.Invoke("Newtonsoft.Json returned null for ui_export");
                        yield break;
                    }

                    onSuccess?.Invoke(parsed);
                }
                catch (Exception e)
                {
                    string err = $"Parse error for ui_export: {e.Message}";
                    Debug.LogError($"[GDAI] {err}");
                    onError?.Invoke(err);
                }
            }
        }

        public IEnumerator FetchUIAtlasSignedUrl(
            string projectId,
            string atlasId,
            Action<string> onSuccess,
            Action<string> onError)
        {
            string url = $"{_supabaseUrl}/functions/v1/bridge-api/v1/projects/{projectId}/ui-export/atlas/{atlasId}";
            Debug.Log($"[GDAI] Fetching ui_export atlas URL from: {url}");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_jwtToken}");
                req.SetRequestHeader("apikey", _anonKey);
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = $"HTTP {req.responseCode}: {req.error}\nBody: {req.downloadHandler.text}";
                    Debug.LogError($"[GDAI] ui_export atlas fetch failed: {err}");
                    onError?.Invoke(err);
                    yield break;
                }

                string payload = req.downloadHandler.text;
                string signedUrl = ExtractSignedUrl(payload);
                if (string.IsNullOrEmpty(signedUrl))
                {
                    onError?.Invoke($"Cannot parse signed URL from response: {payload}");
                    yield break;
                }

                onSuccess?.Invoke(signedUrl);
            }
        }

        /// <summary>
        /// GET text payload from bridge-api path (e.g. template endpoints).
        /// </summary>
        public async Task<string> FetchText(string path)
        {
            string normalizedPath = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
            string url = $"{_supabaseUrl}/functions/v1/bridge-api{normalizedPath}";

            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_jwtToken}");
                req.SetRequestHeader("apikey", _anonKey);
                req.SetRequestHeader("Accept", "text/plain, application/json");

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GDAI] FetchText failed: {req.error} ({url})");
                    return null;
                }

                return req.downloadHandler.text;
            }
        }

        private IEnumerator FetchArtifact<T>(
            string url,
            string artifactType,
            Action<T> onSuccess,
            Action<string> onError)
        {
            Debug.Log($"[GDAI] Fetching {artifactType} from: {url}");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_jwtToken}");
                req.SetRequestHeader("apikey", _anonKey);
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = $"HTTP {req.responseCode}: {req.error}\nBody: {req.downloadHandler.text}";
                    Debug.LogError($"[GDAI] {artifactType} fetch failed: {err}");
                    onError?.Invoke(err);
                    yield break;
                }

                string json = req.downloadHandler.text;
                Debug.Log($"[GDAI] {artifactType} raw JSON ({json.Length} chars):\n{json}");

                try
                {
                    T parsed = JsonUtility.FromJson<T>(json);
                    if (parsed == null)
                    {
                        onError?.Invoke($"JsonUtility returned null for {artifactType}");
                        yield break;
                    }
                    onSuccess?.Invoke(parsed);
                }
                catch (Exception e)
                {
                    string err = $"Parse error for {artifactType}: {e.Message}";
                    Debug.LogError($"[GDAI] {err}");
                    onError?.Invoke(err);
                }
            }
        }

        // JsonUtility cannot deserialize the field name "namespace" directly.
        // Preprocess the payload and map it to "ns" before deserialization.

        private IEnumerator FetchArtifactWithNamespaceWorkaround(
            string url,
            string artifactType,
            Action<CodeIntentResponse> onSuccess,
            Action<string> onError)
        {
            Debug.Log($"[GDAI] Fetching {artifactType} from: {url}");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {_jwtToken}");
                req.SetRequestHeader("apikey", _anonKey);
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = $"HTTP {req.responseCode}: {req.error}\nBody: {req.downloadHandler.text}";
                    Debug.LogError($"[GDAI] {artifactType} fetch failed: {err}");
                    onError?.Invoke(err);
                    yield break;
                }

                string json = req.downloadHandler.text;
                Debug.Log($"[GDAI] {artifactType} raw JSON ({json.Length} chars):\n{json}");

                // WORKAROUND: Replace "namespace": with "ns": so JsonUtility can deserialize.
                // Use a regex that matches "namespace": (with optional whitespace after colon)
                // to avoid accidentally replacing "namespace" appearing inside string values.
                string preprocessed = Regex.Replace(
                    json,
                    "\"namespace\"\\s*:",
                    "\"ns\":"
                );

                try
                {
                    CodeIntentResponse parsed = JsonUtility.FromJson<CodeIntentResponse>(preprocessed);
                    if (parsed == null)
                    {
                        onError?.Invoke($"JsonUtility returned null for {artifactType}");
                        yield break;
                    }
                    onSuccess?.Invoke(parsed);
                }
                catch (Exception e)
                {
                    string err = $"Parse error for {artifactType}: {e.Message}";
                    Debug.LogError($"[GDAI] {err}");
                    onError?.Invoke(err);
                }
            }
        }

        private static string ExtractSignedUrl(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            string trimmed = payload.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(trimmed);
                if (map == null)
                {
                    return null;
                }

                if (map.TryGetValue("signed_url", out var signedUrl) && !string.IsNullOrWhiteSpace(signedUrl))
                {
                    return signedUrl;
                }

                if (map.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }

                if (map.TryGetValue("atlas_url", out var atlasUrl) && !string.IsNullOrWhiteSpace(atlasUrl))
                {
                    return atlasUrl;
                }
            }
            catch
            {
                // Ignore parser errors and return null for caller handling.
            }

            return null;
        }
    }
}
