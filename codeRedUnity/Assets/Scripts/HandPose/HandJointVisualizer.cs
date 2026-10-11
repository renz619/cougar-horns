using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Draws the same four fingers that the classifier reads. No pose data is
    /// invented when tracking is missing, and no sample hand mesh is required.
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Adapter;
        [SerializeField, Tooltip("XR Origin's Camera Offset: the space of tracked joint positions.")]
        Transform m_TrackingSpace;
        [SerializeField] Material m_Material;

        readonly LineRenderer[] m_Fingers = new LineRenderer[4];

        void Start()
        {
            if (m_Adapter == null || m_TrackingSpace == null || m_Material == null)
            {
                Debug.LogError("HandJointVisualizer needs a hand adapter, tracking space, and material.", this);
                enabled = false;
                return;
            }

            var names = new[] { "Index", "Middle", "Ring", "Little" };
            for (var i = 0; i < m_Fingers.Length; ++i)
            {
                var fingerObject = new GameObject($"Tracked {names[i]} Finger");
                fingerObject.transform.SetParent(m_TrackingSpace, false);
                var line = fingerObject.AddComponent<LineRenderer>();
                line.sharedMaterial = m_Material;
                line.useWorldSpace = false;
                line.positionCount = 5;
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
            DrawFinger(m_Fingers[0], hand.index, hand.isTracked);
            DrawFinger(m_Fingers[1], hand.middle, hand.isTracked);
            DrawFinger(m_Fingers[2], hand.ring, hand.isTracked);
            DrawFinger(m_Fingers[3], hand.little, hand.isTracked);
        }

        static void DrawFinger(LineRenderer line, in FingerJoints finger, bool isTracked)
        {
            if (line == null)
                return;

            // This also rejects coincident, NaN, or infinite joint positions.
            line.enabled = isTracked && !float.IsNaN(HandPoseClassifier.FingerCurlDegrees(finger));
            if (!line.enabled)
                return;

            line.SetPosition(0, finger.metacarpal);
            line.SetPosition(1, finger.proximal);
            line.SetPosition(2, finger.intermediate);
            line.SetPosition(3, finger.distal);
            line.SetPosition(4, finger.tip);
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
