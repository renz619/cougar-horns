namespace HandPoseRecognition
{
    public interface IHandFrameSource
    {
        HandSide HandSide { get; }
        // True means available, including an explicitly untracked status frame.
        bool TryGetLatestFrame(out HandFrame frame);
    }
}
