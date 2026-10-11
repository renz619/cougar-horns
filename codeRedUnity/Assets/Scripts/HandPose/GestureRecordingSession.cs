using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum GestureRecordingState { Idle, Recording, ReadyForReview, Canceled }
    public enum GestureRecordingCompletion
    {
        None, ManualStop, FrameLimit, AttemptLimit, Timeout, SourceReplaced,
        CoordinateChanged, InvalidClock, InvalidAcquisition
    }

    /// <summary>Owned, serializable review. An invalid stop remains inspectable but cannot be saved without explicit valid trimming.</summary>
    [Serializable]
    public sealed class GestureRecordingReview
    {
        [SerializeField] GestureClip m_Clip;
        [SerializeField] GestureRecordingCompletion m_Completion;
        [SerializeField] string m_InvalidReason;
        [SerializeField] string[] m_Rejections;
        [SerializeField] int m_Attempts;
        [SerializeField] bool m_Trimmed;
        public GestureRecordingCompletion Completion => m_Completion;
        public string InvalidReason => m_InvalidReason;
        public int AttemptCount => m_Attempts;
        public bool WasTrimmed => m_Trimmed;
        public GestureClip CopyClip() => m_Clip?.Clone();
        public string[] CopyRejections() => m_Rejections == null ? Array.Empty<string>() : (string[])m_Rejections.Clone();

        internal GestureRecordingReview(GestureClip clip, GestureRecordingCompletion completion,
            string invalidReason, string[] rejections, int attempts, bool trimmed = false)
        {
            m_Clip = clip?.Clone(); m_Completion = completion; m_InvalidReason = invalidReason;
            m_Rejections = rejections == null ? Array.Empty<string>() : (string[])rejections.Clone();
            m_Attempts = attempts; m_Trimmed = trimmed;
        }

        public bool TryValidate(out GestureRecognitionResult result, out string error)
        {
            result = default;
            if (!string.IsNullOrEmpty(m_InvalidReason)) { error = m_InvalidReason; return false; }
            if (m_Clip == null) { error = "Review/Clip: no captured evidence."; return false; }
            if (!MotionGestureRecognizer.TryCheckComplete(m_Clip, out result))
            { error = result.Reason; return false; }
            error = null; return true;
        }

        /// <summary>Explicit inclusive trim. Never edits the original review; the complete remaining clip is revalidated.</summary>
        public bool TryTrim(int first, int last, out GestureRecordingReview trimmed, out string error)
        {
            trimmed = null;
            if (m_Clip == null || first < 0 || last < first || last >= m_Clip.Observations.Count)
            { error = "Review/Trim: choose an ordered inclusive frame range."; return false; }
            var observations = new GestureObservation[last - first + 1];
            for (int i = 0; i < observations.Length; i++) observations[i] = m_Clip.Observations[first + i].Clone();
            // Keep the original frozen basis/scale. Extraction rebases time/path from the selected first acquisition.
            var candidate = new GestureRecordingReview(new GestureClip(m_Clip.Definition, m_Clip.Reference, observations),
                m_Completion, null, m_Rejections, m_Attempts, true);
            if (!candidate.TryValidate(out _, out error)) return false;
            trimmed = candidate; return true;
        }
    }

    /// <summary>Manual whole-clip capture. Every distinct acquisition is retained; no static sample interval or ring buffer.</summary>
    public sealed class GestureRecordingSession
    {
        readonly List<GestureObservation> m_Observations = new List<GestureObservation>();
        readonly List<string> m_Rejections = new List<string>();
        GestureDefinition m_Definition;
        GestureReference m_Reference;
        string m_SourceIdentity, m_InvalidReason;
        ulong m_Epoch, m_LastAcquisition, m_LastSequence;
        double m_Start, m_LastNow, m_LastTimestamp, m_LastObservationTime;
        bool m_HasFrame;
        public GestureRecordingState State { get; private set; }
        public GestureRecordingCompletion Completion { get; private set; }
        public int AcceptedCount => m_Observations.Count;
        public int RejectedCount => m_Rejections.Count;
        public int AttemptCount { get; private set; }
        public string LastRejectionReason => m_Rejections.Count == 0 ? null : m_Rejections[m_Rejections.Count - 1];
        public GestureClip CurrentClip => m_Definition == null ? null : new GestureClip(m_Definition, m_Reference, m_Observations.ToArray());

        public bool Begin(GestureDefinition definition, string sourceIdentity, GestureCalibration calibration,
            GestureObservation initial, double now, out string error)
        {
            error = null;
            if (State == GestureRecordingState.Recording) { error = "Stop or cancel the active gesture recording first."; return false; }
            if (definition == null || !Finite(now) || now < 0 || string.IsNullOrWhiteSpace(sourceIdentity))
            { error = "Recording/Begin: definition, source identity and a finite nonnegative clock are required."; return false; }
            if (!definition.TryValidate(out var report)) { error = report.ToString(); return false; }
            if (!definition.IsSupported)
            { error = "Recording/Definition: only one dominant-hand J/Z motion is supported."; return false; }
            if (initial == null || initial.SourceIdentity != sourceIdentity)
            { error = "Recording/Initial: a current acquisition from the selected source is required."; return false; }
            if (!GestureFeatureExtractor.TryFreezeReference(initial, definition, calibration, out var reference, out error)) return false;
            Clear();
            m_Definition = definition.Copy(); m_Reference = reference.Clone(); m_SourceIdentity = sourceIdentity;
            m_Epoch = initial.CoordinateEpoch; m_Start = m_LastNow = now;
            State = GestureRecordingState.Recording;
            Tick(initial, sourceIdentity, m_Epoch, now);
            if (State == GestureRecordingState.ReadyForReview && AcceptedCount == 0)
            { error = m_InvalidReason; return false; }
            return true;
        }

        public void Tick(GestureObservation observation, string currentSourceIdentity, ulong currentCoordinateEpoch, double now)
        {
            if (State != GestureRecordingState.Recording) return;
            if (currentSourceIdentity != m_SourceIdentity)
            { RejectAndFinish(GestureRecordingCompletion.SourceReplaced, "Recording/Source: selected source changed."); return; }
            if (currentCoordinateEpoch != m_Epoch)
            { RejectAndFinish(GestureRecordingCompletion.CoordinateChanged, "Recording/Reference: coordinate epoch changed."); return; }
            if (!Finite(now) || now < m_LastNow)
            { RejectAndFinish(GestureRecordingCompletion.InvalidClock, "Recording/Clock: nonfinite or backwards clock."); return; }
            m_LastNow = now;
            var settings = m_Definition.Settings;
            if (now - m_Start >= settings.MaxDurationSeconds)
            { RejectAndFinish(GestureRecordingCompletion.Timeout, "Recording/Duration: recording window ended without explicit completion."); return; }
            if (observation == null)
            {
                if (m_HasFrame && now - m_LastTimestamp > settings.MaxGapSeconds)
                    RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Gap: no required acquisition within the maximum gap.");
                return;
            }
            if (observation.SourceIdentity != m_SourceIdentity || observation.CoordinateEpoch != m_Epoch)
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Observation: source or coordinate epoch differs from Begin."); return; }
            if (!observation.TryValidate(out var observationReason))
            { AttemptCount++; RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, observationReason); return; }
            var hand = observation.GetHand(m_Definition.DominantSide);
            if (!m_HasFrame || hand == null || observation.AcquisitionId != m_LastAcquisition || hand.SequenceId != m_LastSequence) AttemptCount++;
            if (!Finite(observation.TimestampSeconds) || observation.TimestampSeconds < 0 || observation.TimestampSeconds > now
                || now - observation.TimestampSeconds > settings.MaxFrameAgeSeconds || hand == null)
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Observation: missing, future or stale required acquisition."); return; }
            if (!hand.IsTracked || hand.HandSide != m_Definition.DominantSide || hand.Space != HandFrameSpace.TrackingSpace
                || hand.TimestampSeconds > now || now - hand.TimestampSeconds > settings.MaxFrameAgeSeconds)
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Hand: untracked, wrong-side, non-raw, future or stale evidence."); return; }
            if (m_HasFrame && (observation.AcquisitionId < m_LastAcquisition || hand.SequenceId < m_LastSequence))
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Sequence: acquisition or source sequence moved backwards."); return; }
            if (m_HasFrame && (observation.AcquisitionId == m_LastAcquisition || hand.SequenceId == m_LastSequence))
            {
                if (now - m_LastTimestamp > settings.MaxGapSeconds)
                    RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Gap: repeated reads cannot bridge a missing acquisition.");
                return;
            }
            if (m_HasFrame && (hand.TimestampSeconds <= m_LastTimestamp || observation.TimestampSeconds <= m_LastObservationTime
                || hand.TimestampSeconds - m_LastTimestamp > settings.MaxGapSeconds))
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, "Recording/Time: backwards/nonadvancing timestamp or tracking gap."); return; }
            // Validate only the incoming pair; retained immutable evidence was already admitted.
            if (!GestureFeatureExtractor.TryValidateObservation(observation, m_Definition, m_Reference,
                m_HasFrame ? m_Observations[m_Observations.Count - 1] : null, out var reason))
            { RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, reason); return; }
            m_Observations.Add(observation); m_LastAcquisition = observation.AcquisitionId; m_LastSequence = hand.SequenceId;
            m_LastTimestamp = hand.TimestampSeconds; m_LastObservationTime = observation.TimestampSeconds; m_HasFrame = true;
            if (AcceptedCount >= settings.MaxFrames)
                RejectAndFinish(GestureRecordingCompletion.FrameLimit, "Recording/Frames: frame cap reached; a truncated clip is not a complete example.");
            else if (AttemptCount >= settings.MaxAttempts)
                RejectAndFinish(GestureRecordingCompletion.AttemptLimit, "Recording/Attempts: attempt cap reached without explicit completion.");
        }

        void RejectAndFinish(GestureRecordingCompletion completion, string reason)
        { m_InvalidReason = reason; m_Rejections.Add(reason); Completion = completion; State = GestureRecordingState.ReadyForReview; }
        public void Stop()
        { if (State == GestureRecordingState.Recording) { Completion = GestureRecordingCompletion.ManualStop; State = GestureRecordingState.ReadyForReview; } }
        public void Invalidate(string reason)
        { if (State == GestureRecordingState.Recording) RejectAndFinish(GestureRecordingCompletion.InvalidAcquisition, reason); }
        public void Cancel() { Clear(); State = GestureRecordingState.Canceled; }
        public void Clear()
        {
            m_Observations.Clear(); m_Rejections.Clear(); m_Definition = null; m_Reference = null; m_SourceIdentity = m_InvalidReason = null;
            m_HasFrame = false; AttemptCount = 0; Completion = GestureRecordingCompletion.None; State = GestureRecordingState.Idle;
        }
        public GestureRecordingReview ExportReview()
        {
            if (State != GestureRecordingState.ReadyForReview) throw new InvalidOperationException("Stop gesture recording before review.");
            return new GestureRecordingReview(new GestureClip(m_Definition, m_Reference, m_Observations.ToArray()),
                Completion, m_InvalidReason, m_Rejections.ToArray(), AttemptCount);
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
