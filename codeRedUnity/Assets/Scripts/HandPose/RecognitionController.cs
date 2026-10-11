using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Unity source/clock/catalog bridge. Only explicit rebuilds create runtime snapshots.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RecognitionController : MonoBehaviour
    {
        [SerializeField] MonoBehaviour m_Source;
        [SerializeField] PoseDatabase m_Database;
        [SerializeField] RecognitionBackend m_Backend = RecognitionBackend.LegacyRules;
        [SerializeField] RecognitionTimingSettings m_Timing = new RecognitionTimingSettings();
        PoseDatabaseSnapshot m_Snapshot;
        PoseDatabase m_BuiltDatabase;
        readonly RecognitionSession m_Session = new RecognitionSession();
        public RecognitionBackend Backend
        {
            get => m_Backend;
            set { if (m_Backend != value) { m_Backend = value; m_Session.Reset("Backend changed."); } }
        }
        public MonoBehaviour Source { get => m_Source; set { if (m_Source != value) { m_Source = value; m_Session.Reset("Source changed."); } } }
        public PoseDatabase Database { get => m_Database; set { if (m_Database != value) { m_Database = value; m_Snapshot = null; m_Session.Reset("Database changed; explicit rebuild required."); } } }
        public RecognitionSession Session => m_Session;
        public string CatalogReport { get; private set; }
        public bool LastBuildSucceeded { get; private set; }
        public event Action<RecognitionResult> PoseConfirmed;
        public string ConfirmedDisplayName => m_Session.ConfirmedPoseId != null && m_Snapshot != null
            && m_Snapshot.TryGetPose(m_Session.ConfirmedPoseId, out var pose) ? pose.DisplayName : null;

        public bool RebuildCatalog(bool retainPreviousOnFailure = false)
        {
            m_BuiltDatabase = m_Database;
            PoseDatabaseSnapshot replacement = null;
            ValidationReport report = null;
            bool valid = m_Database != null && m_Database.TryBuildSnapshot(out replacement, out report);
            LastBuildSucceeded = valid;
            CatalogReport = valid ? "Valid snapshot built." : report == null ? "No database assigned." : report.ToString();
            if (valid || !retainPreviousOnFailure) { m_Snapshot = valid ? replacement : null; m_Session.Reset(CatalogReport); }
            else CatalogReport = "Reload failed; explicitly retaining previous snapshot.\n" + CatalogReport;
            return valid;
        }
        void Forward(RecognitionResult result) => PoseConfirmed?.Invoke(result);
        void OnEnable() { m_Session.Reset(); m_Session.PoseConfirmed += Forward; RebuildCatalog(); }
        void OnDisable() { m_Session.PoseConfirmed -= Forward; m_Session.Reset(); }
        void Update()
        {
            if (m_BuiltDatabase != m_Database)
            {
                m_BuiltDatabase = m_Database; m_Snapshot = null;
                LastBuildSucceeded = false; CatalogReport = "Database reference changed; explicit rebuild required.";
                m_Session.Reset(CatalogReport);
            }
            HandFrame raw = null;
            object identity = null;
            if (m_Source != null && m_Source.isActiveAndEnabled && m_Source is IHandFrameSource source)
            { identity = m_Source; source.TryGetLatestFrame(out raw); }
            m_Session.Update(identity, raw, m_Snapshot, m_Backend, m_Timing, Time.realtimeSinceStartupAsDouble);
        }
    }
}
