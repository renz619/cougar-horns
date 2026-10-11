namespace HandPoseRecognition
{
    public enum RecognitionStatus
    {
        Unavailable, InvalidSettings, InvalidFrame, Untracked, Stale, UnsupportedFrame,
        CatalogUnavailable, NoCandidates, NoEligibleSamples, AboveThreshold, Matched,
        NormalizationFailed
    }

    /// <summary>Observation only. Default is unavailable; Error is meaningful only when HasScore is true.</summary>
    public readonly struct RecognitionResult
    {
        public RecognitionStatus Status { get; }
        public string Reason { get; }
        public string PoseId { get; }
        public string BestCandidateId { get; }
        public double Error { get; }
        public bool HasScore { get; }
        public bool IsMatch => HasScore && Status == RecognitionStatus.Matched;
        public HandSide HandSide { get; }
        public double TimestampSeconds { get; }
        public ulong SequenceId { get; }
        public int SampleIndex { get; }

        internal RecognitionResult(RecognitionStatus status, HandFrame frame, string reason = null,
            string candidate = null, double error = 0, int sampleIndex = -1)
        {
            Status = status; Reason = reason; BestCandidateId = candidate;
            HasScore = status == RecognitionStatus.Matched || status == RecognitionStatus.AboveThreshold;
            PoseId = status == RecognitionStatus.Matched ? candidate : null;
            Error = error; SampleIndex = sampleIndex;
            HandSide = frame == null ? HandSide.Unknown : frame.HandSide;
            TimestampSeconds = frame == null ? 0 : frame.TimestampSeconds;
            SequenceId = frame == null ? 0 : frame.SequenceId;
        }
    }

    internal static class FrameAdmission
    {
        internal static RecognitionStatus Check(HandFrame frame, double now, double maxAge, bool canonical)
        {
            if (!PoseMath.IsFinite(now) || now < 0 || !PoseMath.IsFinite(maxAge) || maxAge <= 0)
                return RecognitionStatus.InvalidSettings;
            if (frame == null) return RecognitionStatus.InvalidFrame;
            if (!frame.IsTracked) return RecognitionStatus.Untracked;
            if (!PoseMath.IsFinite(frame.TimestampSeconds) || frame.TimestampSeconds < 0 || frame.TimestampSeconds > now)
                return RecognitionStatus.InvalidFrame;
            if (now - frame.TimestampSeconds > maxAge) return RecognitionStatus.Stale;
            if (frame.Space != (canonical ? HandFrameSpace.CanonicalHandSpace : HandFrameSpace.TrackingSpace)
                || frame.JointLayoutVersion != 1 || frame.NormalizationVersion != (canonical ? 1 : 0))
                return RecognitionStatus.UnsupportedFrame;
            if (!frame.IsValid || !PoseDataValidation.KnownSide(frame.HandSide)) return RecognitionStatus.InvalidFrame;
            return RecognitionStatus.Matched; // Admission success, not a geometric match.
        }
    }
}
