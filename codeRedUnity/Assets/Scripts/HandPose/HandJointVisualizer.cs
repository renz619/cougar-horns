using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Draws all five digits from the adapter's measured joint positions.
    /// No pose data is invented when tracking is missing, and no sample hand
    /// mesh is required.
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Adapter;
        [SerializeField, Tooltip("XR Origin's Camera Offset: the space of tracked joint positions.")]
        Transform m_TrackingSpace;
        [SerializeField] Material m_Material;

        readonly LineRenderer[] m_Fingers = new LineRenderer[5];

        void Start()
        {
            if (m_Adapter == null || m_TrackingSpace == null || m_Material == null)
            {
                Debug.LogError("HandJointVisualizer needs a hand adapter, tracking space, and material.", this);
                enabled = false;
                return;
            }

            var names = new[] { "Index", "Middle", "Ring", "Little", "Thumb" };
            for (var i = 0; i < m_Fingers.Length; ++i)
            {
                var fingerObject = new GameObject($"Tracked {names[i]} Finger");
                fingerObject.transform.SetParent(m_TrackingSpace, false);
                var line = fingerObject.AddComponent<LineRenderer>();
                line.sharedMaterial = m_Material;
                line.useWorldSpace = false;
                line.positionCount = i == 4 ? 4 : 5;
                line.startWidth = line.endWidth = 0.005f;
                line.numCapVertices = 4;
                line.numCornerVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                m_Fingers[i] = line;
            }
        }

        void LateUpdate()
        {
            if (m_Adapter == null || !m_Adapter.isActiveAndEnabled)
            {
                HideFingers();
                return;
            }

            var hand = m_Adapter.currentHand;
            DrawFinger(m_Fingers[0], hand.isTracked, hand.index.isValid,
                hand.index.metacarpal, hand.index.proximal, hand.index.distal, hand.index.tip, hand.index.intermediate);
            DrawFinger(m_Fingers[1], hand.isTracked, hand.middle.isValid,
                hand.middle.metacarpal, hand.middle.proximal, hand.middle.distal, hand.middle.tip, hand.middle.intermediate);
            DrawFinger(m_Fingers[2], hand.isTracked, hand.ring.isValid,
                hand.ring.metacarpal, hand.ring.proximal, hand.ring.distal, hand.ring.tip, hand.ring.intermediate);
            DrawFinger(m_Fingers[3], hand.isTracked, hand.little.isValid,
                hand.little.metacarpal, hand.little.proximal, hand.little.distal, hand.little.tip, hand.little.intermediate);
            DrawFinger(m_Fingers[4], hand.isTracked, hand.thumb.isValid,
                hand.thumb.metacarpal, hand.thumb.proximal, hand.thumb.distal, hand.thumb.tip);
        }

        static void DrawFinger(LineRenderer line, bool isTracked, bool isValid,
            Vector3 metacarpal, Vector3 proximal, Vector3 distal, Vector3 tip, Vector3? intermediate = null)
        {
            if (line == null)
                return;

            // All digits share the same geometry checks. The thumb has no
            // intermediate joint, so its outline connects proximal to distal.
            var middle = intermediate ?? proximal;
            line.enabled = isTracked && isValid
                && IsUsableSegment(proximal - metacarpal)
                && (!intermediate.HasValue || IsUsableSegment(middle - proximal))
                && IsUsableSegment(distal - middle)
                && IsUsableSegment(tip - distal);
            if (!line.enabled)
                return;

            line.SetPosition(0, metacarpal);
            line.SetPosition(1, proximal);
            if (intermediate.HasValue)
                line.SetPosition(2, middle);
            line.SetPosition(line.positionCount - 2, distal);
            line.SetPosition(line.positionCount - 1, tip);
        }

        static bool IsUsableSegment(Vector3 segment)
        {
            var length = segment.magnitude;
            return !float.IsNaN(length) && !float.IsInfinity(length) && length >= 1e-4f;
        }

        void OnDisable() => HideFingers();

        void HideFingers()
        {
            foreach (var line in m_Fingers)
                if (line != null)
                    line.enabled = false;
        }

        void OnDestroy()
        {
            foreach (var line in m_Fingers)
                if (line != null)
                {
                    if (Application.isPlaying)
                        Destroy(line.gameObject);
                    else
                        DestroyImmediate(line.gameObject);
                }
        }
    }
}
