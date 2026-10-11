using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum RecordingState { Idle, Recording, ReadyForReview, Canceled }
    public enum RecordingCompletion { None, ManualStop, SampleLimit, AttemptLimit, Timeout, SourceReplaced, InvalidClock }

    [Serializable]
    public sealed class PoseRecordingSettings
    {
        public string PoseId = "recorded.shape";
        public string DisplayName = "Recorded shape";
        public HandSide Side = HandSide.Right;
        public float[] Weights = HandPoseData.CreateDefaultWeights();
        public float MaxError = .1f;
        public int MaxSamplesPerPose = 32;
        public int MaxAcceptedSamples = 32;
        public int MaxAttempts = 256;
        public double MaxDurationSeconds = 10;
        public double MinimumSampleIntervalSeconds = .1;
        public double MaxFrameAgeSeconds = .25;

        internal PoseRecordingSettings Copy()
        {
            var copy = (PoseRecordingSettings)MemberwiseClone();
            copy.Weights = Weights == null ? null : (float[])Weights.Clone();
            return copy;
        }

        public bool TryValidate(out string error)
        {
            var report = new ValidationReport();
            PoseDataValidation.ValidateWeights(Weights, "Recording/Weights", report);
            if (string.IsNullOrWhiteSpace(PoseId) || PoseId != PoseId.Trim()) report.Add("Recording/PoseId", "Nonblank trimmed ID required.");
            if (string.IsNullOrWhiteSpace(DisplayName)) report.Add("Recording/DisplayName", "Nonblank display name required.");
            if (!PoseDataValidation.KnownSide(Side)) report.Add("Recording/Side", "Known side required.");
            if (!PoseMath.IsFinite(MaxError) || MaxError < 0) report.Add("Recording/MaxError", "Finite nonnegative threshold required.");
            if (MaxSamplesPerPose < 1 || MaxAcceptedSamples < 1 || MaxAcceptedSamples > MaxSamplesPerPose)
                report.Add("Recording/SampleLimits", "Positive accepted limit must fit the template budget.");
            if (MaxAttempts < 1) report.Add("Recording/MaxAttempts", "Positive attempt budget required.");
            if (!PoseMath.IsFinite(MaxDurationSeconds) || MaxDurationSeconds <= 0) report.Add("Recording/Duration", "Finite positive duration required.");
            if (!PoseMath.IsFinite(MinimumSampleIntervalSeconds) || MinimumSampleIntervalSeconds < 0)
                report.Add("Recording/Interval", "Finite nonnegative interval required.");
            if (!PoseMath.IsFinite(MaxFrameAgeSeconds) || MaxFrameAgeSeconds <= 0) report.Add("Recording/Freshness", "Finite positive age required.");
            error = report.IsValid ? null : report.ToString();
            return report.IsValid;
        }
    }

    /// <summary>Owned serializable review payload. Editor tooling persists this, never a live asset, before Play Mode exits.</summary>
    [Serializable]
    public sealed class PoseRecordingReview
    {
        [SerializeField] PoseRecordingSettings m_Definition;
        [SerializeField] PoseSample[] m_Samples;
        public PoseRecordingSettings CopyDefinition() => m_Definition?.Copy();
        public PoseSample[] CopySamples()
        {
            if (m_Samples == null) return Array.Empty<PoseSample>();
            var copy = new PoseSample[m_Samples.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = m_Samples[i].Clone();
            return copy;
        }
        internal PoseRecordingReview(PoseRecordingSettings settings, List<PoseSample> samples)
        {
            m_Definition = settings.Copy();
            m_Samples = new PoseSample[samples.Count];
            for (int i = 0; i < samples.Count; i++) m_Samples[i] = samples[i].Clone();
        }
    }

    /// <summary>Independent bounded capture. Tick uses a monotonic injected clock, including when no acquisition exists.</summary>
    public sealed class PoseRecordingSession
    {
        readonly List<PoseSample> m_Samples = new List<PoseSample>();
        readonly HashSet<ulong> m_Seen = new HashSet<ulong>();
        PoseRecordingSettings m_Settings;
        object m_Source;
        double m_Start, m_LastNow, m_LastAcceptedTimestamp;
        public RecordingState State { get; private set; }
        public RecordingCompletion Completion { get; private set; }
        public int AcceptedCount => m_Samples.Count;
        public int RejectedCount { get; private set; }
        public int AttemptCount { get; private set; }
        public string LastRejectionReason { get; private set; }

        public bool Begin(PoseRecordingSettings settings, object sourceIdentity, double now, out string error)
        {
            error = null;
            if (State == RecordingState.Recording) { error = "Stop or cancel the active session first."; return false; }
            if (settings == null || sourceIdentity == null || !PoseMath.IsFinite(now) || now < 0)
            { error = "Settings, source identity and a finite nonnegative clock are required."; return false; }
            if (!settings.TryValidate(out error)) return false;
            Clear();
            m_Settings = settings.Copy(); m_Source = sourceIdentity; m_Start = m_LastNow = now;
            State = RecordingState.Recording;
            return true;
        }

        public void Tick(object sourceIdentity, HandFrame raw, double now)
        {
            if (State != RecordingState.Recording) return;
            if (!ReferenceEquals(m_Source, sourceIdentity)) { Finish(RecordingCompletion.SourceReplaced); return; }
            if (!PoseMath.IsFinite(now) || now < m_LastNow) { Finish(RecordingCompletion.InvalidClock); return; }
            m_LastNow = now;
            if (now - m_Start >= m_Settings.MaxDurationSeconds) { Finish(RecordingCompletion.Timeout); return; }
            if (raw == null || !m_Seen.Add(raw.SequenceId)) return;
            AttemptCount++;
            var admission = FrameAdmission.Check(raw, now, m_Settings.MaxFrameAgeSeconds, false);
            if (raw.HandSide != m_Settings.Side) Reject("Frame side differs from frozen recording side.");
            else if (admission != RecognitionStatus.Matched) Reject(admission.ToString());
            else if (!HandPoseNormalizer.TryNormalizeFrame(raw, out var canonical, out var reason)) Reject(reason);
            else if (AcceptedCount > 0 && raw.TimestampSeconds - m_LastAcceptedTimestamp < m_Settings.MinimumSampleIntervalSeconds)
                Reject("Sample interval not reached (or acquisition time moved backwards).");
            else if (!PoseSample.TryCreate(canonical, m_Settings.Weights, out var sample, out var report)) Reject(report.ToString());
            else { m_Samples.Add(sample); m_LastAcceptedTimestamp = raw.TimestampSeconds; }
            if (AcceptedCount >= m_Settings.MaxAcceptedSamples) Finish(RecordingCompletion.SampleLimit);
            else if (AttemptCount >= m_Settings.MaxAttempts) Finish(RecordingCompletion.AttemptLimit);
        }

        void Reject(string reason) { RejectedCount++; LastRejectionReason = reason; }
        void Finish(RecordingCompletion reason) { Completion = reason; State = RecordingState.ReadyForReview; }
        public void Stop() { if (State == RecordingState.Recording) Finish(RecordingCompletion.ManualStop); }
        public void Cancel() { Clear(); State = RecordingState.Canceled; }
        public void Clear()
        {
            m_Samples.Clear(); m_Seen.Clear(); m_Settings = null; m_Source = null;
            AttemptCount = RejectedCount = 0; LastRejectionReason = null;
            Completion = RecordingCompletion.None; State = RecordingState.Idle;
        }
        public PoseRecordingReview ExportReview()
        {
            if (State != RecordingState.ReadyForReview) throw new InvalidOperationException("Stop recording before exporting review data.");
            return new PoseRecordingReview(m_Settings, m_Samples);
        }
    }
}
