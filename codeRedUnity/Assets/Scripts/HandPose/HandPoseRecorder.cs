using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Thin acquisition/clock bridge. Recording begins only through an explicit Begin call.</summary>
    public sealed class HandPoseRecorder : MonoBehaviour
    {
        [SerializeField] MonoBehaviour m_Source;
        [SerializeField] PoseRecordingSettings m_Settings = new PoseRecordingSettings();
        public PoseRecordingSession Session { get; } = new PoseRecordingSession();
        public MonoBehaviour Source { get => m_Source; set => m_Source = value; }
        public PoseRecordingSettings Settings => m_Settings;
        public bool Begin(out string error)
        {
            if (!isActiveAndEnabled || !(m_Source is IHandFrameSource) || !m_Source.isActiveAndEnabled)
            { error = "An enabled recorder and IHandFrameSource component are required."; return false; }
            return Session.Begin(m_Settings, m_Source, Time.realtimeSinceStartupAsDouble, out error);
        }
        public void Stop() => Session.Stop();
        public void Cancel() => Session.Cancel();
        void Update()
        {
            HandFrame raw = null;
            if (m_Source != null && m_Source.isActiveAndEnabled && m_Source is IHandFrameSource source)
                source.TryGetLatestFrame(out raw);
            Session.Tick(m_Source == null ? null : m_Source, raw, Time.realtimeSinceStartupAsDouble);
        }
        void OnDisable() => Session.Stop();
    }
}
