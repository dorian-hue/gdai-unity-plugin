using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GDAI.Bridge;
using GDAI.Runtime.UI;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GDAI.Bridge.Editor
{
    /// <summary>
    /// Imports UI export metadata into Resources for runtime DataBinder loading.
    /// </summary>
    public static class UIMetadataImporter
    {
        private const string ResourcesUiFolder = "Assets/GDAI/Resources/GDAI/UI";
        private const string ResourcesAtlasFolder = "Assets/GDAI/Resources/GDAI/UI/Atlases";

        public static IEnumerator ImportFromBridge(
            string supabaseUrl,
            string anonKey,
            string jwtToken,
            string projectId,
            Action<int, int> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl) ||
                string.IsNullOrWhiteSpace(anonKey) ||
                string.IsNullOrWhiteSpace(jwtToken) ||
                string.IsNullOrWhiteSpace(projectId))
            {
                onError?.Invoke("UIMetadataImporter missing required parameters.");
                yield break;
            }

            var client = new GDAIBridgeClient(supabaseUrl, anonKey, jwtToken);
            UIExportPackage package = null;
            string packageError = null;

            yield return client.FetchUIExportPackage(
                projectId,
                parsed => package = parsed,
                err => packageError = err);

            if (package == null)
            {
                onError?.Invoke(string.IsNullOrWhiteSpace(packageError)
                    ? "UI export request returned null package."
                    : packageError);
                yield break;
            }

            if (package?.screens == null || package.screens.Count == 0)
            {
                Debug.Log("[GDAI] No UI screens to import.");
                onSuccess?.Invoke(0, 0);
                yield break;
            }

            EnsureDirectory(ResourcesUiFolder);
            EnsureDirectory(ResourcesAtlasFolder);

            int screenCount = WriteScreens(package.screens, onError);
            if (screenCount <= 0)
            {
                yield break;
            }

            int atlasCount = 0;
            var importedAtlasIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var screen in package.screens)
            {
                if (screen?.atlas == null || string.IsNullOrWhiteSpace(screen.atlas.atlas_id))
                {
                    continue;
                }

                string atlasId = screen.atlas.atlas_id;
                if (!importedAtlasIds.Add(atlasId))
                {
                    continue;
                }

                string signedUrl = null;
                string signedUrlError = null;
                yield return client.FetchUIAtlasSignedUrl(
                    projectId,
                    atlasId,
                    url => signedUrl = url,
                    err => signedUrlError = err);
                if (string.IsNullOrWhiteSpace(signedUrl))
                {
                    if (!string.IsNullOrWhiteSpace(signedUrlError))
                    {
                        onError?.Invoke(signedUrlError);
                    }

                    continue;
                }

                byte[] pngBytes = null;
                yield return GetBytes(signedUrl, data => pngBytes = data, error =>
                {
                    onError?.Invoke($"Atlas download failed for '{atlasId}': {error}");
                });

                if (pngBytes == null || pngBytes.Length == 0)
                {
                    continue;
                }

                string atlasPath = Path.Combine(ResourcesAtlasFolder, $"{atlasId}.png");
                try
                {
                    File.WriteAllBytes(atlasPath, pngBytes);
                    atlasCount++;
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed writing atlas '{atlasId}': {e.Message}");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[GDAI] UI Metadata: imported {screenCount} screens, {atlasCount} atlases");
            onSuccess?.Invoke(screenCount, atlasCount);
        }

        private static int WriteScreens(List<UIExportScreen> screens, Action<string> onError)
        {
            int count = 0;
            foreach (var screen in screens)
            {
                if (screen == null || string.IsNullOrWhiteSpace(screen.screen_type))
                {
                    continue;
                }

                string filePath = Path.Combine(ResourcesUiFolder, $"{screen.screen_type}.json");
                try
                {
                    string json = JsonConvert.SerializeObject(screen, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                    count++;
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed writing screen '{screen.screen_type}': {e.Message}");
                }
            }

            return count;
        }

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static IEnumerator GetBytes(
            string url,
            Action<byte[]> onSuccess,
            Action<string> onError)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 30;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"HTTP {req.responseCode}: {req.error}");
                    yield break;
                }

                onSuccess?.Invoke(req.downloadHandler.data);
            }
        }
    }
}
