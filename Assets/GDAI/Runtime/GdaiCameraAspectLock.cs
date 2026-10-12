using System;
using UnityEngine;

namespace GDAI.Runtime
{
    /// <summary>Persists the generated camera's aspect intent across scene loads.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-32000)]
    [ExecuteAlways]
    public sealed class GdaiCameraAspectLock : MonoBehaviour
    {
        [SerializeField] private float targetAspect;

        public float TargetAspect => targetAspect;

        public void SetTargetAspect(float value)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            targetAspect = value;
            Apply();
        }

        public void Apply()
        {
            if (targetAspect <= 0f || float.IsNaN(targetAspect) || float.IsInfinity(targetAspect))
                throw new InvalidOperationException("saved camera aspect missing/invalid");
            var camera = GetComponent<Camera>();
            if (camera == null) throw new InvalidOperationException("generated camera missing");
            camera.aspect = targetAspect;
        }

        // Unity may invoke these while the composer is first adding an unconfigured shell.
        private void Awake() { if (targetAspect != 0f) Apply(); }
        private void OnEnable() { if (targetAspect != 0f) Apply(); }
    }
}
