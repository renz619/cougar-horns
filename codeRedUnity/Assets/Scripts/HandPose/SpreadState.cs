namespace HandPoseRecognition
{
    /// <summary>
    /// Whether two adjacent extended fingers are held together or spread apart.
    /// </summary>
    /// <remarks>
    /// Unknown is listed first so it is the default value (0): a pair whose
    /// fingers aren't both extended, whose data is invalid, or whose spread
    /// falls between the thresholds is never reported as Together or Apart.
    /// </remarks>
    public enum SpreadState
    {
        Unknown,
        Together,
        Apart,
    }
}
