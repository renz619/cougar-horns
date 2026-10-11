using System;
using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Reads actual XR acquisitions and runs the simple camera-relative movement rules.</summary>
    public sealed class DirectHandGestureController : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Adapter;
        [SerializeField] Transform m_TrackingSpace;
        [SerializeField] Transform m_Head;
        [SerializeField] TMP_Text m_Label;
        [SerializeField] HandPoseClassifierSettings m_Settings = new HandPoseClassifierSettings();
        [SerializeField] DirectGestureSettings m_MotionSettings = new DirectGestureSettings();
        [SerializeField, Min(.1f)] float m_ResultDisplaySeconds = 2f;
        readonly DirectGestureRecognizer m_Recognizer = new DirectGestureRecognizer();
        ulong m_Sequence;
        double m_LastSampleTime = -1;
        double m_DetectedAt = -10;
        Vector3 m_ViewOrigin;
        Quaternion m_ViewRotation;
        Matrix4x4 m_LastRig;
        bool m_HasRig;
        QuestHandAdapter m_LastAdapter;
        Transform m_LastHead;
        HandSide m_LastSide;
        HandPose m_LastShape;
        public DirectGesture LastDetected { get; private set; }
        public event Action<DirectGesture> GestureDetected;
        public void ResetRecognition() => ResetTracking("Start the indicated gesture.");

        void OnEnable() => ResetTracking();
        void OnDisable()
        {
            ResetTracking();
            if (m_Label != null) m_Label.text = "GESTURES DISABLED";
        }

        void ResetTracking(string reason = "Waiting for hand tracking.")
        {
            m_Recognizer.Reset(reason); m_Sequence = 0; m_LastSampleTime = -1;
            m_HasRig = false; m_LastShape = HandPose.Unknown;
        }

        void Update()
        {
            if (m_Adapter == null || !m_Adapter.isActiveAndEnabled || m_TrackingSpace == null || m_Head == null
                || m_Settings == null || m_MotionSettings == null || !m_MotionSettings.IsValid())
            { ResetTracking("Assign hand, camera, tracking space, and valid settings."); RefreshLabel(); return; }
            var rig = m_TrackingSpace.localToWorldMatrix;
            if (m_LastAdapter != m_Adapter || m_LastHead != m_Head || m_LastSide != m_Adapter.HandSide
                || (m_HasRig && !SameRig(rig, m_LastRig))) ResetTracking("Reference changed; start a new gesture.");
            m_LastAdapter = m_Adapter; m_LastHead = m_Head; m_LastSide = m_Adapter.HandSide;
            m_LastRig = rig; m_HasRig = true;
            if (!m_Adapter.TryGetLatestFrame(out var frame) || frame == null || !frame.IsValid
                || frame.Space != HandFrameSpace.TrackingSpace || frame.HandSide != m_Adapter.HandSide
                || Time.realtimeSinceStartupAsDouble - frame.TimestampSeconds > m_MotionSettings.maximumSampleGap)
            { ResetTracking(); RefreshLabel(); return; }
            if (frame.SequenceId == m_Sequence) { RefreshLabel(); return; }
            m_Sequence = frame.SequenceId;
            var hand = HandFrameLegacyProjection.Project(frame);
            var shape = HandPoseClassifier.Classify(hand, m_Settings);
            if (shape != m_LastShape || (m_LastSampleTime >= 0
                && frame.TimestampSeconds - m_LastSampleTime > m_MotionSettings.maximumSampleGap))
                m_Recognizer.Reset("Begin an open-hand gesture or fist nod.");
            m_LastShape = shape; m_LastSampleTime = frame.TimestampSeconds;
            if (m_Recognizer.SampleCount == 0)
            { m_ViewOrigin = m_Head.position; m_ViewRotation = m_Head.rotation; }
            if (!TryBuildSample(frame, rig, m_ViewOrigin, m_ViewRotation, m_Settings, out var sample))
                m_Recognizer.Reset("Required palm/finger joints unavailable; show your hand.");
            else
            {
                var result = m_Recognizer.Update(sample, m_MotionSettings);
                if (result != DirectGesture.None)
                {
                    LastDetected = result; m_DetectedAt = Time.unscaledTimeAsDouble;
                    GestureDetected?.Invoke(result);
                    Debug.Log("[DirectHandGestureController] " + DirectGestureRecognizer.LabelFor(result) + " detected", this);
                }
            }
            RefreshLabel();
        }

        public static bool TryBuildSample(HandFrame frame, Matrix4x4 trackingToWorld, Vector3 viewOrigin,
            Quaternion viewRotation, HandPoseClassifierSettings settings, out DirectGestureSample sample)
        {
            sample = default;
            if (frame == null || !frame.IsValid || frame.Space != HandFrameSpace.TrackingSpace || settings == null
                || !PoseMath.IsFinite(viewOrigin) || !PoseMath.TryNormalize(viewRotation, out var rotation)
                || !frame.TryGetJoint(HandJointId.Palm, out var palm)
                || !frame.TryGetJoint(HandJointId.Wrist, out var wrist)
                || !frame.TryGetJoint(HandJointId.MiddleProximal, out var knuckle)) return false;
            var inverse = Quaternion.Inverse(rotation);
            var position = inverse * (trackingToWorld.MultiplyPoint3x4(palm.Position) - viewOrigin);
            var direction = inverse * trackingToWorld.MultiplyVector(knuckle.Position - wrist.Position);
            if (!PoseMath.IsFinite(position) || !PoseMath.IsFinite(direction) || direction.sqrMagnitude < 1e-8f) return false;
            float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            var hand = HandFrameLegacyProjection.Project(frame);
            sample = new DirectGestureSample(frame.TimestampSeconds, position, pitch, HandPoseClassifier.Classify(hand, settings));
            return sample.Shape == HandPose.Open || sample.Shape == HandPose.Fist;
        }

        static bool SameRig(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (!PoseMath.IsFinite(a[i]) || Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }

        void RefreshLabel()
        {
            if (m_Label == null) return;
            bool showing = LastDetected != DirectGesture.None && Time.unscaledTimeAsDouble - m_DetectedAt < m_ResultDisplaySeconds;
            string text = (showing ? DirectGestureRecognizer.LabelFor(LastDetected) + " DETECTED" : "GESTURES: UNKNOWN")
                + "\n<size=50%>" + m_Recognizer.Status + "</size>";
            if (m_Label.text != text) m_Label.text = text;
        }
    }
}
