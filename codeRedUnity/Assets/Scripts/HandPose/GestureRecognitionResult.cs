namespace HandPoseRecognition
{
    public enum GestureRecognitionStatus
    { Unavailable, InvalidClip, Unsupported, Incomplete, CompleteClip, NoCandidates, AboveThreshold, Matched }

    /// <summary>A geometric observation, separate from lifecycle confirmation. Default has no score or ID.</summary>
    public readonly struct GestureRecognitionResult
    {
        public GestureRecognitionStatus Status { get; }
        public string GestureId { get; }
        public string BestCandidateId { get; }
        public string Reason { get; }
        public bool HasScore { get; }
        public bool IsMatch => Status == GestureRecognitionStatus.Matched && HasScore;
        public double Error { get; }
        public int ExampleIndex { get; }
        public double PhaseCoverage { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        public HandSide DominantSide { get; }
        public GestureHandMask ParticipatingHands { get; }
        public GestureHandRoles Roles { get; }
        public string SourceIdentity { get; }
        public ulong CoordinateEpoch { get; }
        public ulong LastAcquisitionId { get; }
        internal GestureRecognitionResult(GestureRecognitionStatus status, GestureClip clip, string reason,
            double coverage = 0, string candidate = null, double error = 0, int index = -1)
        {
            Status = status; Reason = reason; PhaseCoverage = coverage; BestCandidateId = candidate;
            HasScore = status == GestureRecognitionStatus.Matched || status == GestureRecognitionStatus.AboveThreshold;
            GestureId = status == GestureRecognitionStatus.Matched ? candidate : null; Error = error; ExampleIndex = index;
            StartTime = clip?.StartTime ?? 0; EndTime = StartTime + (clip?.DurationSeconds ?? 0);
            DominantSide = clip?.FrozenDefinition?.DominantSide ?? HandSide.Unknown;
            ParticipatingHands = DominantSide == HandSide.Right ? GestureHandMask.Right : DominantSide == HandSide.Left ? GestureHandMask.Left : GestureHandMask.None;
            Roles = clip?.FrozenDefinition?.RequiredRoles ?? GestureHandRoles.None;
            SourceIdentity = clip?.Reference?.SourceIdentity; CoordinateEpoch = clip?.Reference?.CoordinateEpoch ?? 0;
            LastAcquisitionId = clip != null && clip.Observations.Count > 0 ? clip.Observations[clip.Observations.Count - 1]?.AcquisitionId ?? 0 : 0;
        }
    }
}
