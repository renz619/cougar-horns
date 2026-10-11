using System;

namespace HandPoseRecognition
{
    /// <summary>
    /// Optional push view of the existing source contract. Motion consumers receive every
    /// Dynamic acquisition and explicit loss publication, without changing snapshot ownership.
    /// Latest-frame readers remain compatible with IHandFrameSource.
    /// </summary>
    public interface IHandFrameStream : IHandFrameSource
    {
        event Action<HandFrame> FramePublished;
    }
}
