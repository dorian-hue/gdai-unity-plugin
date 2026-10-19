using System;
using System.Collections.Generic;
using System.IO;
using GDAI.Runtime.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GDAI.Bridge.Editor
{
    /// <summary>
    /// Builds UGUI screen prefabs from GDAI UI export JSON.
    /// </summary>
    public static class GDAIScreenPrefabBuilder
    {
        public static GameObject BuildFromJson(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"UI JSON not found: {jsonPath}");
            }

            string json = File.ReadAllText(jsonPath);
            var screen = JsonConvert.DeserializeObject<JObject>(json);
            if (screen == null)
            {
                throw new InvalidOperationException($"Cannot parse UI JSON: {jsonPath}");
            }

            string screenType = screen["screen_type"]?.ToString() ?? "unknown";
            string slotId = screen["slot_id"]?.ToString() ?? string.Empty;
            string slotPrefix = slotId.Length >= 8 ? slotId.Substring(0, 8) : slotId;

            var canvasGo = new GameObject($"{PascalCase(screenType)}_{slotPrefix}_Screen");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            TryApplyReferenceResolution(scaler, screen);
            canvasGo.AddComponent<GraphicRaycaster>();

            var binder = canvasGo.AddComponent<GDAIDataBinder>();
            binder.screenType = screenType;

            BuildNodes(canvasGo.transform, screen);
            return canvasGo;
        }

        private static void BuildNodes(Transform root, JObject screen)
        {
            var nodes = (screen["nodes"] as JArray) ?? (screen["layout"]?["nodes"] as JArray);
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            var pathLookup = new Dictionary<string, Transform>(StringComparer.Ordinal)
            {
                { string.Empty, root }
            };
            var usedNames = new Dictionary<string, int>(StringComparer.Ordinal);
            var pending = new List<JToken>(nodes);

            bool progressed = true;
            while (pending.Count > 0 && progressed)
            {
                progressed = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var node = pending[i];
                    string parentPath = node["parent_path"]?.ToString() ?? string.Empty;
                    if (!pathLookup.TryGetValue(parentPath, out var parent))
                    {
                        continue;
                    }

                    var go = CreateNode(node);
                    go.transform.SetParent(parent, false);

                    string requestedName = node["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(requestedName))
                    {
                        requestedName = go.name;
                    }

                    string uniqueName = EnsureUniqueName(requestedName, usedNames);
                    go.name = uniqueName;

                    string myPath = string.IsNullOrEmpty(parentPath)
                        ? uniqueName
                        : $"{parentPath}/{uniqueName}";
                    pathLookup[myPath] = go.transform;

                    ApplyLayout(go, node);
                    ApplyStyle(go, node);

                    pending.RemoveAt(i);
                    progressed = true;
                }
            }

            // Fallback: attach unresolved nodes to root to avoid blocking the whole screen build.
            foreach (var unresolved in pending)
            {
                var go = CreateNode(unresolved);
                go.transform.SetParent(root, false);
                string requestedName = unresolved["name"]?.ToString() ?? go.name;
                go.name = EnsureUniqueName(requestedName, usedNames);
                ApplyLayout(go, unresolved);
                ApplyStyle(go, unresolved);
                Debug.LogWarning($"[GDAI] Parent path missing, attached node to root: {go.name}");
            }
        }

        private static GameObject CreateNode(JToken node)
        {
            string type = node["type"]?.ToString() ?? "panel";
            var go = new GameObject(type);
            go.AddComponent<RectTransform>();

            switch (type.ToLowerInvariant())
            {
                case "text":
                case "label":
                {
                    var tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.text = node["content"]?.ToString() ?? node["label"]?.ToString() ?? string.Empty;
                    tmp.fontSize = ReadFloat(node, 14f, "font_size");
                    break;
                }
                case "image":
                case "icon":
                {
                    var img = go.AddComponent<Image>();
                    img.color = ParseColor(node["color"]?.ToString()) ?? Color.white;
                    break;
                }
                case "button":
                {
                    var image = go.AddComponent<Image>();
                    image.color = ParseColor(node["color"]?.ToString()) ?? Color.white;
                    go.AddComponent<Button>();

                    string btnLabel = node["label"]?.ToString() ?? node["content"]?.ToString();
                    if (!string.IsNullOrEmpty(btnLabel))
                    {
                        CreateButtonLabel(go.transform, btnLabel, ReadFloat(node, 14f, "font_size"));
                    }

                    break;
                }
                case "slider":
                case "progress_bar":
                {
                    var bg = go.AddComponent<Image>();
                    bg.color = ParseColor(node["color"]?.ToString()) ?? new Color(0.2f, 0.2f, 0.2f, 1f);

                    var slider = go.AddComponent<Slider>();
                    slider.direction = Slider.Direction.LeftToRight;
                    CreateSliderChildren(go, slider);
                    break;
                }
                default:
                {
                    var panel = go.AddComponent<Image>();
                    panel.color = ParseColor(node["color"]?.ToString()) ?? new Color(0f, 0f, 0f, 0f);
                    break;
                }
            }

            return go;
        }

        private static void CreateButtonLabel(Transform parent, string text, float fontSize)
        {
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(parent, false);

            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var btnText = textGo.AddComponent<TextMeshProUGUI>();
            btnText.text = text;
            btnText.alignment = TextAlignmentOptions.Center;
            btnText.fontSize = fontSize;
            btnText.color = Color.black;
        }

        private static void ApplyLayout(GameObject go, JToken node)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            string anchor = node["anchor"]?.ToString() ?? "center";
            switch (NormalizeAnchor(anchor))
            {
                case "topleft":
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    break;
                case "topcenter":
                case "top":
                    rect.anchorMin = new Vector2(0.5f, 1f);
                    rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    break;
                case "topright":
                    rect.anchorMin = new Vector2(1f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(1f, 1f);
                    break;
                case "middleleft":
                case "left":
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(0f, 0.5f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    break;
                case "middleright":
                case "right":
                    rect.anchorMin = new Vector2(1f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    break;
                case "bottomleft":
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.zero;
                    rect.pivot = Vector2.zero;
                    break;
                case "bottomcenter":
                case "bottom":
                    rect.anchorMin = new Vector2(0.5f, 0f);
                    rect.anchorMax = new Vector2(0.5f, 0f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    break;
                case "bottomright":
                    rect.anchorMin = new Vector2(1f, 0f);
                    rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(1f, 0f);
                    break;
                case "stretchall":
                case "stretch":
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    return;
                default:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    break;
            }

            float x = ReadFloat(node, 0f, "x", "position.x");
            float y = ReadFloat(node, 0f, "y", "position.y");
            float width = ReadFloat(node, 100f, "width", "size.x", "min_width");
            float height = ReadFloat(node, 40f, "height", "size.y", "min_height");

            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void ApplyStyle(GameObject go, JToken node)
        {
            var color = ParseColor(node["color"]?.ToString());
            if (!color.HasValue)
            {
                return;
            }

            var image = go.GetComponent<Image>();
            if (image != null)
            {
                image.color = color.Value;
            }

            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.color = color.Value;
            }
        }

        private static void TryApplyReferenceResolution(CanvasScaler scaler, JToken screen)
        {
            float width = ReadFloat(screen, 1920f, "reference_resolution.x", "layout.reference_resolution.x");
            float height = ReadFloat(screen, 1080f, "reference_resolution.y", "layout.reference_resolution.y");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);
        }

        private static void CreateSliderChildren(GameObject parent, Slider slider)
        {
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(parent.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.2f, 0.8f, 0.4f, 1f);

            slider.fillRect = fillRect;
            slider.targetGraphic = parent.GetComponent<Image>();
            slider.value = 1f;
        }

        private static float ReadFloat(JToken token, float fallback, params string[] paths)
        {
            if (token == null)
            {
                return fallback;
            }

            foreach (var path in paths)
            {
                var selected = token.SelectToken(path);
                if (selected != null && selected.Type != JTokenType.Null && float.TryParse(selected.ToString(), out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        private static string EnsureUniqueName(string requested, IDictionary<string, int> nameCounts)
        {
            if (!nameCounts.TryGetValue(requested, out int count))
            {
                nameCounts[requested] = 0;
                return requested;
            }

            count++;
            nameCounts[requested] = count;
            return $"{requested}_{count}";
        }

        private static string NormalizeAnchor(string anchor)
        {
            return string.IsNullOrWhiteSpace(anchor)
                ? "center"
                : anchor.ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        }

        private static Color? ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return null;
            }

            string input = hex.Trim();
            if (!input.StartsWith("#", StringComparison.Ordinal))
            {
                input = "#" + input;
            }

            if (ColorUtility.TryParseHtmlString(input, out var color))
            {
                return color;
            }

            return null;
        }

        private static string PascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var parts = value.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var result = string.Empty;
            foreach (var part in parts)
            {
                if (part.Length == 1)
                {
                    result += char.ToUpperInvariant(part[0]);
                }
                else
                {
                    result += char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
                }
            }

            return result;
        }
    }
}
