namespace HandPoseRecognition
{
    /// <summary>
    /// The judged state of a single finger or the thumb.
    /// </summary>
    /// <remarks>
    /// Unknown is listed first so it is the default value (0): missing,
    /// invalid or in-between data is never reported as Extended or Curled.
    /// </remarks>
    public enum FingerState
    {
        Unknown,
        Extended,
        Curled,
    }
}
