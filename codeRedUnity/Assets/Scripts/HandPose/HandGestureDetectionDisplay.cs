using System;
using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Session-latched confirmation feedback, independent of the live diagnostic label.</summary>
    public sealed class HandGestureDetectionDisplay : MonoBehaviour
    {
        [SerializeField] HandGestureController m_Controller;
        [SerializeField] TMP_Text m_Label;
        [SerializeField] string m_GestureId = "asl.letter.z";
        [SerializeField] string m_DetectedText = "Z DETECTED";
        [SerializeField, Min(0)] float m_FlashDuration = 1.5f;
        [SerializeField, Min(.05f)] float m_FlashInterval = .25f;
        HandGestureController m_BoundController;
        bool m_HasDetected;
        double m_FlashStarted;

        public HandGestureController Controller { get => m_Controller; set { m_Controller = value; Bind(); } }
        public TMP_Text Label { get => m_Label; set { m_Label = value; RefreshLabel(); } }
        public bool HasDetected => m_HasDetected;

        void Awake() { m_HasDetected = false; RefreshLabel(); }
        void OnEnable() { Bind(); RefreshLabel(); }
        void OnDisable()
        {
            Unbind();
            // Disabling motion or this listener never clears an already confirmed session result.
            if (m_Label != null && m_HasDetected) { m_Label.enabled = true; m_Label.color = Color.red; }
        }
        void Bind()
        {
            var next = isActiveAndEnabled ? m_Controller : null;
            if (m_BoundController == next) return;
            Unbind(); m_BoundController = next;
            if (m_BoundController != null) m_BoundController.GestureConfirmed += Confirmed;
        }
        void Unbind()
        {
            if (m_BoundController != null) m_BoundController.GestureConfirmed -= Confirmed;
            m_BoundController = null;
        }
        void Confirmed(GestureRecognitionResult result)
        {
            if (!result.IsMatch || !string.Equals(result.GestureId, m_GestureId, StringComparison.Ordinal)) return;
            m_HasDetected = true; m_FlashStarted = Time.unscaledTimeAsDouble;
            RefreshLabel();
        }
        void Update() { Bind(); RefreshLabel(); }
        void RefreshLabel()
        {
            if (m_Label == null) return;
            m_Label.enabled = m_HasDetected;
            if (!m_HasDetected) return;
            m_Label.text = m_DetectedText;
            double elapsed = Time.unscaledTimeAsDouble - m_FlashStarted;
            double duration = PoseMath.IsFinite(m_FlashDuration) ? Math.Max(0, m_FlashDuration) : 1.5;
            double interval = PoseMath.IsFinite(m_FlashInterval) ? Math.Max(.05, m_FlashInterval) : .25;
            var color = Color.red;
            if (elapsed >= 0 && elapsed < duration && (long)(elapsed / interval) % 2 != 0) color.a = 0;
            m_Label.color = color;
        }
    }
}
