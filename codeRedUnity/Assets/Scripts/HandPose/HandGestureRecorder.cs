using UnityEngine;

namespace HandPoseRecognition
{
    public sealed class HandGestureRecorder : MonoBehaviour
    {
        [SerializeField] MonoBehaviour m_Source;
        [SerializeField] GestureReferenceProvider m_ReferenceProvider;
        [SerializeField] GestureDefinition m_Definition = new GestureDefinition();
        readonly GestureRecordingSession m_Session = new GestureRecordingSession();
        HandGestureInput m_Input;
        public MonoBehaviour Source { get => m_Source; set => m_Source = value; }
        public GestureReferenceProvider ReferenceProvider { get => m_ReferenceProvider; set => m_ReferenceProvider = value; }
        public GestureDefinition Definition { get => m_Definition; set => m_Definition = value; }
        public GestureRecordingSession Session => m_Session;
        void OnEnable() { m_Input = new HandGestureInput(Process); Configure(); }
        void OnDisable() { m_Input?.Dispose(); m_Input = null; m_Session.Stop(); }
        void Configure() { m_Input?.Configure(m_Source, m_ReferenceProvider); }
        public bool Begin(out string reason)
        {
            if (!isActiveAndEnabled || m_Input == null) { reason = "Enable the gesture recorder before beginning."; return false; }
            Configure();
            if (m_ReferenceProvider == null) { reason = "Assign the gesture reference provider."; return false; }
            if (!m_ReferenceProvider.TryGetCalibration(out var calibration, out reason)) return false;
            return m_Session.Begin(m_Definition, m_Input.SourceIdentity, calibration, m_Input.Read(), Time.realtimeSinceStartupAsDouble, out reason);
        }
        void Update() { Configure(); Process(m_Input?.Read()); }
        void Process(GestureObservation observation)
        {
            if (m_Session.State != GestureRecordingState.Recording || m_Input == null) return;
            if (m_Source == null || !m_Source.isActiveAndEnabled)
            { m_Session.Invalidate("Recording/Source: required source disabled or destroyed."); return; }
            if (m_ReferenceProvider == null || !m_ReferenceProvider.TryGetCalibration(out _, out _))
            { m_Session.Invalidate("Recording/Calibration: reference became unavailable."); return; }
            m_Session.Tick(observation, m_Input.SourceIdentity, m_ReferenceProvider.CoordinateEpoch, Time.realtimeSinceStartupAsDouble);
        }
        public void Stop() => m_Session.Stop();
        public void Cancel() => m_Session.Cancel();
    }
}
