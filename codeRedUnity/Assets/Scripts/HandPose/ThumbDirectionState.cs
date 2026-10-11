namespace HandPoseRecognition
{
    /// <summary>
    /// Whether the thumb points up in Unity world space (+Y).
    /// </summary>
    /// <remarks>
    /// Unknown is listed first so it is the default value (0): missing joints,
    /// a missing tracking-space rotation, degenerate geometry or an invalid
    /// angle setting are never reported as Up or NotUp.
    /// </remarks>
    public enum ThumbDirectionState
    {
        Unknown,
        Up,
        NotUp,
    }
}
