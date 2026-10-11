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

        // v0.2a poses, resolved by HandPoseClassifier.ClassifyPose. Values are
        // explicit so the earlier ones are never renumbered.
        Pointing = 3,
        V = 4,
        U = 5,
        Y = 6,
        L = 7,

        // v0.2b pose, resolved by HandPoseClassifier.ClassifyPoseWithOrientation.
        ThumbsUp = 8,
    }
}
