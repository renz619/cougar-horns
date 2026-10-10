namespace HandPoseRecognition
{
    /// <summary>
    /// The result of classifying one hand.
    /// </summary>
    /// <remarks>
    /// Unknown is listed first so it is the enum's default value (0). An
    /// uninitialized HandPose field therefore means "no valid gesture" rather
    /// than accidentally meaning Open or Fist.
    /// </remarks>
    public enum HandPose
    {
        Unknown,
        Open,
        Fist,
    }
}
