using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GDAI.Runtime.UI
{
    /// <summary>
    /// Attach to a Canvas. Reads metadata JSON and binds UGUI controls automatically.
    /// Game code only needs to register data providers and action handlers.
    /// </summary>
    public class GDAIDataBinder : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Screen type (hud, main_menu, etc)")]
        public string screenType;

        [Tooltip("Override metadata source. Default: Resources/GDAI/UI/{screenType}.json")]
        public TextAsset metadataOverride;

        private static readonly Dictionary<string, Func<object>> DataProviders = new Dictionary<string, Func<object>>();
        private static readonly Dictionary<string, Action<string>> ActionHandlers = new Dictionary<string, Action<string>>();

        private UIExportScreen _screen;
        private readonly List<DataBindingEntry> _activeDataBindings = new List<DataBindingEntry>();

        private struct DataBindingEntry
        {
            public string source;
            public Component target;
            public string bindingType;
            public string maxSource;
        }

        public static void RegisterData(string source, Func<object> provider)
        {
            if (string.IsNullOrWhiteSpace(source) || provider == null)
            {
                return;
            }

            DataProviders[source] = provider;
        }

        public static void RegisterAction(string dispatch, Action<string> handler)
        {
            if (string.IsNullOrWhiteSpace(dispatch) || handler == null)
            {
                return;
            }

            ActionHandlers[dispatch] = handler;
        }

        public static void ClearAll()
        {
            DataProviders.Clear();
            ActionHandlers.Clear();
        }

        private void Start()
        {
            LoadMetadata();
            if (_screen == null)
            {
                return;
            }

            BindAll();
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _activeDataBindings.Count; i++)
            {
                var entry = _activeDataBindings[i];
                if (!DataProviders.TryGetValue(entry.source, out var provider))
                {
                    continue;
                }

                var value = provider();
                ApplyValue(entry, value);
            }
        }

        private void LoadMetadata()
        {
            string json;
            if (metadataOverride != null)
            {
                json = metadataOverride.text;
            }
            else
            {
                var asset = Resources.Load<TextAsset>($"GDAI/UI/{screenType}");
                if (asset == null)
                {
                    Debug.LogWarning($"[GDAIDataBinder] No metadata for '{screenType}'. Expected Resources/GDAI/UI/{screenType}.json");
                    return;
                }

                json = asset.text;
            }

            try
            {
                _screen = JsonConvert.DeserializeObject<UIExportScreen>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GDAIDataBinder] Failed to parse metadata: {e.Message}");
                _screen = null;
            }
        }

        private void BindAll()
        {
            if (_screen.bindings == null)
            {
                Debug.LogWarning($"[GDAIDataBinder] '{screenType}' has no bindings.");
                return;
            }

            int behaviorCount = 0;
            foreach (var binding in _screen.bindings)
            {
                if (binding == null)
                {
                    continue;
                }

                if (binding.group == "data")
                {
                    BindData(binding);
                }
                else if (binding.group == "behavior")
                {
                    BindBehavior(binding);
                    behaviorCount++;
                }
            }

            Debug.Log($"[GDAIDataBinder] {screenType}: {_activeDataBindings.Count} data bindings, {behaviorCount} behavior bindings");
        }

        private void BindData(UIExportBinding binding)
        {
            var target = FindUIComponent(binding.node_name);
            if (target == null)
            {
                Debug.LogWarning($"[GDAIDataBinder] UI element not found: '{binding.node_name}'");
                return;
            }

            string maxSource = null;
            if (!string.IsNullOrEmpty(binding.extras_json))
            {
                try
                {
                    var extras = JsonConvert.DeserializeObject<Dictionary<string, string>>(binding.extras_json);
                    if (extras != null)
                    {
                        extras.TryGetValue("max_source", out maxSource);
                    }
                }
                catch
                {
                    // extras are optional and can be malformed; skip without failing binding.
                }
            }

            _activeDataBindings.Add(new DataBindingEntry
            {
                source = binding.source_or_dispatch,
                target = target,
                bindingType = binding.binding_type,
                maxSource = maxSource
            });
        }

        private void BindBehavior(UIExportBinding binding)
        {
            var go = FindUIGameObject(binding.node_name);
            if (go == null)
            {
                Debug.LogWarning($"[GDAIDataBinder] UI element not found: '{binding.node_name}'");
                return;
            }

            string dispatch = binding.source_or_dispatch;
            string argsJson = binding.args_json;

            var button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() =>
                {
                    if (ActionHandlers.TryGetValue(dispatch, out var handler))
                    {
                        handler(argsJson);
                    }
                    else
                    {
                        Debug.LogWarning($"[GDAIDataBinder] No handler for '{dispatch}'");
                    }
                });
                return;
            }

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                slider.onValueChanged.AddListener(value =>
                {
                    if (ActionHandlers.TryGetValue(dispatch, out var handler))
                    {
                        handler(value.ToString());
                    }
                    else
                    {
                        Debug.LogWarning($"[GDAIDataBinder] No handler for '{dispatch}'");
                    }
                });
                return;
            }

            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(value =>
                {
                    if (ActionHandlers.TryGetValue(dispatch, out var handler))
                    {
                        handler(value.ToString());
                    }
                    else
                    {
                        Debug.LogWarning($"[GDAIDataBinder] No handler for '{dispatch}'");
                    }
                });
            }
        }

        private void ApplyValue(DataBindingEntry entry, object value)
        {
            if (value == null || entry.target == null)
            {
                return;
            }

            try
            {
                switch (entry.bindingType)
                {
                    case "progress":
                        ApplyProgress(entry, value);
                        break;
                    case "number":
                        SetText(entry.target, Convert.ToSingle(value).ToString("N0"));
                        break;
                    case "string":
                        SetText(entry.target, value.ToString());
                        break;
                    case "boolean":
                        if (entry.target is Toggle toggle)
                        {
                            toggle.isOn = Convert.ToBoolean(value);
                        }
                        else
                        {
                            entry.target.gameObject.SetActive(Convert.ToBoolean(value));
                        }

                        break;
                    case "percentage":
                        if (entry.target is Slider slider)
                        {
                            slider.value = Convert.ToSingle(value);
                        }
                        else
                        {
                            SetText(entry.target, $"{Convert.ToSingle(value) * 100f:F0}%");
                        }

                        break;
                }
            }
            catch
            {
                // Skip conversion failures to keep runtime resilient to mismatched source types.
            }
        }

        private void ApplyProgress(DataBindingEntry entry, object value)
        {
            float current = Convert.ToSingle(value);
            float max = 100f;
            if (!string.IsNullOrEmpty(entry.maxSource) &&
                DataProviders.TryGetValue(entry.maxSource, out var maxProvider))
            {
                max = Convert.ToSingle(maxProvider());
            }

            if (entry.target is Slider slider)
            {
                slider.maxValue = max;
                slider.value = current;
                return;
            }

            if (entry.target is Image image)
            {
                image.fillAmount = max > 0f ? current / max : 0f;
            }
        }

        private static void SetText(Component target, string text)
        {
            if (target is TMP_Text tmp)
            {
                tmp.text = text;
            }
            else if (target is Text legacyText)
            {
                legacyText.text = text;
            }
        }

        private Component FindUIComponent(string nodeName)
        {
            var go = FindUIGameObject(nodeName);
            if (go == null)
            {
                return null;
            }

            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                return tmp;
            }

            var text = go.GetComponent<Text>();
            if (text != null)
            {
                return text;
            }

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                return slider;
            }

            var image = go.GetComponent<Image>();
            if (image != null)
            {
                return image;
            }

            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                return toggle;
            }

            var button = go.GetComponent<Button>();
            if (button != null)
            {
                return button;
            }

            return go.transform;
        }

        private GameObject FindUIGameObject(string nodeName)
        {
            var found = FindDeep(transform, nodeName);
            return found != null ? found.gameObject : null;
        }

        private static Transform FindDeep(Transform parent, string targetName)
        {
            if (parent.name == targetName)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindDeep(parent.GetChild(i), targetName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
