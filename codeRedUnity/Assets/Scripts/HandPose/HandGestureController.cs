using System;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum GestureLessonMode { Disabled, JZOnly }

    /// <summary>Opt-in J/Z lesson arbitration. Existing static components/settings remain independent.</summary>
    public sealed class HandGestureController : MonoBehaviour
    {
        [SerializeField] MonoBehaviour m_Source;
        [SerializeField] GestureReferenceProvider m_ReferenceProvider;
        [SerializeField] GestureCatalog m_Catalog;
        [SerializeField] GestureLessonMode m_Mode = GestureLessonMode.Disabled;
        readonly GestureRecognitionSession m_Session = new GestureRecognitionSession();
        HandGestureInput m_Input;
        GestureCatalogSnapshot m_Snapshot;
        GestureCatalog m_BuiltCatalog;
        GestureLessonMode m_LastMode;
        MonoBehaviour m_LastSource;
        GestureReferenceProvider m_LastReference;
        public MonoBehaviour Source { get => m_Source; set => m_Source = value; }
        public GestureReferenceProvider ReferenceProvider { get => m_ReferenceProvider; set => m_ReferenceProvider = value; }
        public GestureCatalog Catalog { get => m_Catalog; set { if (m_Catalog != value) { m_Catalog = value; m_Snapshot = null; m_Session.Reset("Catalog changed; explicitly rebuild."); } } }
        public GestureLessonMode Mode { get => m_Mode; set { if (m_Mode != value) { m_Mode = value; m_Session.Reset("Gesture lesson mode changed."); } } }
        public GestureRecognitionSession Session => m_Session;
        public string CatalogReport { get; private set; }
        public bool LastBuildSucceeded { get; private set; }
        public string ConfirmedDisplayName => m_Session.ConfirmedGestureId != null && m_Snapshot != null && m_Snapshot.TryGetGesture(m_Session.ConfirmedGestureId, out var entry)
            ? entry.Definition.DisplayName : null;
        public event Action<GestureRecognitionResult> GestureConfirmed;
        public bool RebuildCatalog(bool retainPreviousOnFailure = false)
        {
            m_BuiltCatalog = m_Catalog; GestureCatalogSnapshot replacement = null; ValidationReport report = null;
            LastBuildSucceeded = m_Catalog != null && m_Catalog.TryBuildSnapshot(out replacement, out report);
            CatalogReport = LastBuildSucceeded ? "Valid gesture catalog built." : report?.ToString() ?? "Assign a gesture catalog.";
            if (LastBuildSucceeded || !retainPreviousOnFailure) { m_Snapshot = replacement; m_Session.Reset(CatalogReport); }
            else CatalogReport = "Reload failed; explicitly retaining the previous immutable catalog.\n" + CatalogReport;
            return LastBuildSucceeded;
        }
        void OnEnable()
        {
            m_Session.StateChanged += LogStateChange;
            m_Session.Reset(); m_Session.GestureConfirmed += Forward;
            m_Input = new HandGestureInput(Process); Configure(); RebuildCatalog();
        }
        void OnDisable() { m_Input?.Dispose(); m_Input = null; m_Session.GestureConfirmed -= Forward; m_Session.Reset("Motion component disabled."); m_Session.StateChanged -= LogStateChange; }
        void LogStateChange(GestureRecognitionState previous, GestureRecognitionState next, string reason)
        {
            Debug.Log($"[HandGestureController] {previous} -> {next}; gesture={m_Session.ConfirmedGestureId ?? m_Session.PendingGestureId ?? "none"}; {reason}", this);
        }
        void Forward(GestureRecognitionResult result) => GestureConfirmed?.Invoke(result);
        void Configure()
        {
            if (m_LastSource != m_Source || m_LastReference != m_ReferenceProvider || m_LastMode != m_Mode)
            { m_Session.Reset("Gesture source/reference/mode changed."); m_LastSource = m_Source; m_LastReference = m_ReferenceProvider; m_LastMode = m_Mode; }
            if (m_BuiltCatalog != m_Catalog) { m_Snapshot = null; m_BuiltCatalog = m_Catalog; LastBuildSucceeded = false; CatalogReport = "Catalog changed; explicit rebuild required."; m_Session.Reset(CatalogReport); }
            m_Input?.Configure(m_Source, m_ReferenceProvider);
        }
        void Update() { Configure(); Process(m_Input?.Read()); }
        void Process(GestureObservation observation)
        {
            Configure();
            if (!isActiveAndEnabled || m_Mode != GestureLessonMode.JZOnly) return;
            GestureCalibration calibration = null;
            if (m_ReferenceProvider != null) m_ReferenceProvider.TryGetCalibration(out calibration, out _);
            m_Session.Update(observation, calibration, m_Snapshot, Time.realtimeSinceStartupAsDouble);
        }
    }
}
