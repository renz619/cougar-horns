using System;

namespace HandPoseRecognition
{
    public enum RecognitionBackend { LegacyRules, Template }
    public enum RecognitionControllerStatus { Unavailable, LegacyRules, InvalidSettings, ObservationFailure, WaitingForEvaluation, Holding, Confirmed, NoMatch }

    [Serializable]
    public sealed class RecognitionTimingSettings
    {
        public double MaxFrameAgeSeconds = .25;
        public double MinimumEvaluationIntervalSeconds = .1;
        public double ConfirmationDwellSeconds = .2;
        public double EventCooldownSeconds = .5;
        public bool IsValid => PoseMath.IsFinite(MaxFrameAgeSeconds) && MaxFrameAgeSeconds > 0
            && PoseMath.IsFinite(MinimumEvaluationIntervalSeconds) && MinimumEvaluationIntervalSeconds >= 0
            && PoseMath.IsFinite(ConfirmationDwellSeconds) && ConfirmationDwellSeconds >= 0
            && PoseMath.IsFinite(EventCooldownSeconds) && EventCooldownSeconds >= 0;
    }

    /// <summary>Timing policy with injected source, snapshot and clock. Reset/re-enable resets event cooldown.</summary>
    public sealed class RecognitionSession
    {
        object m_Source;
        PoseDatabaseSnapshot m_Catalog;
        RecognitionBackend m_Backend;
        bool m_HasContext, m_HasSequence, m_HasEvaluation, m_HasEvent;
        ulong m_Sequence, m_EvaluatedSequence;
        HandFrame m_Canonical;
        double m_LastNow, m_LastEvaluation, m_PendingSince, m_LastEvent;
        bool m_HoldEventEmitted;
        public RecognitionResult LatestObservation { get; private set; }
        public RecognitionControllerStatus Status { get; private set; }
        public string Reason { get; private set; }
        public string PendingPoseId { get; private set; }
        public string ConfirmedPoseId { get; private set; }
        public event Action<RecognitionResult> PoseConfirmed;

        public void Reset(string reason = "Disabled or reset.")
        {
            m_HasContext = m_HasSequence = m_HasEvaluation = m_HasEvent = false;
            m_Source = null; m_Catalog = null; m_Canonical = null;
            LatestObservation = default; ClearHold();
            Status = RecognitionControllerStatus.Unavailable; Reason = reason; m_LastNow = 0;
        }

        void ClearHold() { PendingPoseId = ConfirmedPoseId = null; m_HoldEventEmitted = false; }
        void Fail(RecognitionStatus status, HandFrame raw, string reason)
        {
            ClearHold(); m_Canonical = null;
            LatestObservation = new RecognitionResult(status, raw, reason);
            Status = status == RecognitionStatus.InvalidSettings ? RecognitionControllerStatus.InvalidSettings : RecognitionControllerStatus.ObservationFailure;
            Reason = reason;
        }

        public void Update(object sourceIdentity, HandFrame raw, PoseDatabaseSnapshot catalog,
            RecognitionBackend backend, RecognitionTimingSettings settings, double now)
        {
            if (!m_HasContext || !ReferenceEquals(sourceIdentity, m_Source) || !ReferenceEquals(catalog, m_Catalog) || backend != m_Backend)
            {
                Reset("Recognition context changed.");
                m_HasContext = true; m_Source = sourceIdentity; m_Catalog = catalog; m_Backend = backend;
            }
            if (backend == RecognitionBackend.LegacyRules)
            { Status = RecognitionControllerStatus.LegacyRules; Reason = "Legacy classifier remains in HandPoseDisplay."; return; }
            if (backend != RecognitionBackend.Template || settings == null || !settings.IsValid
                || !PoseMath.IsFinite(now) || now < 0 || now < m_LastNow)
            { Fail(RecognitionStatus.InvalidSettings, raw, "Invalid timing settings or nonmonotonic clock."); return; }
            m_LastNow = now;
            if (sourceIdentity == null) { Fail(RecognitionStatus.Unavailable, raw, "Source unavailable."); return; }
            var admission = FrameAdmission.Check(raw, now, settings.MaxFrameAgeSeconds, false);
            if (admission != RecognitionStatus.Matched) { Fail(admission, raw, admission.ToString()); return; }
            if (catalog == null) { Fail(RecognitionStatus.CatalogUnavailable, raw, "Catalog unavailable; build a validated snapshot."); return; }
            // Source sequences advance monotonically in a source lifetime. Older acquisitions never become new evidence.
            bool newSequence = !m_HasSequence || raw.SequenceId > m_Sequence;
            if (newSequence)
            {
                m_HasSequence = true; m_Sequence = raw.SequenceId;
                if (!HandPoseNormalizer.TryNormalizeFrame(raw, out m_Canonical, out string reason))
                { Fail(RecognitionStatus.NormalizationFailed, raw, reason); return; }
            }
            if (m_Canonical == null) return; // A repeated failed acquisition cannot recover by being reread.
            if (raw.SequenceId != m_Sequence) { Fail(RecognitionStatus.InvalidFrame, raw, "Acquisition sequence moved backwards."); return; }
            if (m_HasEvaluation && m_EvaluatedSequence == m_Sequence)
            { TryEmit(now, settings); return; }
            if (m_HasEvaluation && now - m_LastEvaluation < settings.MinimumEvaluationIntervalSeconds)
            {
                Reason = "Waiting for evaluation interval.";
                if (ConfirmedPoseId == null && PendingPoseId == null) Status = RecognitionControllerStatus.WaitingForEvaluation;
                TryEmit(now, settings);
                return;
            }
            m_HasEvaluation = true; m_LastEvaluation = now; m_EvaluatedSequence = m_Sequence;
            LatestObservation = ShapeRecognizer.Recognize(m_Canonical, catalog, now, settings.MaxFrameAgeSeconds);
            if (!LatestObservation.IsMatch)
            { ClearHold(); Status = RecognitionControllerStatus.NoMatch; Reason = LatestObservation.Reason; return; }
            string id = LatestObservation.PoseId;
            if (!string.Equals(id, PendingPoseId, StringComparison.Ordinal))
            { ClearHold(); PendingPoseId = id; m_PendingSince = LatestObservation.TimestampSeconds; }
            // Dwell is supported by acquisition timestamps, not by delaying evaluation of two nearly simultaneous frames.
            if (LatestObservation.TimestampSeconds - m_PendingSince >= settings.ConfirmationDwellSeconds)
            { ConfirmedPoseId = id; Status = RecognitionControllerStatus.Confirmed; Reason = "Confirmed shape."; TryEmit(now, settings); }
            else { Status = RecognitionControllerStatus.Holding; Reason = "Hold for distinct-acquisition confirmation."; }
        }

        void TryEmit(double now, RecognitionTimingSettings settings)
        {
            if (ConfirmedPoseId == null || m_HoldEventEmitted || !LatestObservation.IsMatch
                || LatestObservation.PoseId != ConfirmedPoseId || now - LatestObservation.TimestampSeconds > settings.MaxFrameAgeSeconds
                || (m_HasEvent && now - m_LastEvent < settings.EventCooldownSeconds)) return;
            m_HoldEventEmitted = true; m_HasEvent = true; m_LastEvent = now;
            PoseConfirmed?.Invoke(LatestObservation);
        }
    }
}
