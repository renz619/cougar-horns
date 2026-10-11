using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HandPoseRecognition
{
    /// <summary>Copied immutable runtime template. Contains no Unity object/asset reference.</summary>
    public sealed class PoseTemplate
    {
        public int SchemaVersion { get; }
        public string PoseId { get; }
        public string DisplayName { get; }
        public HandSide HandSide { get; }
        public int JointLayoutVersion { get; }
        public int NormalizationVersion { get; }
        public float MaxError { get; }
        readonly ReadOnlyCollection<float> m_Weights;
        readonly ReadOnlyCollection<PoseSample> m_Samples;
        public IReadOnlyList<float> JointWeights => m_Weights;
        public IReadOnlyList<PoseSample> Samples => m_Samples;

        internal PoseTemplate(HandPoseData source)
        {
            SchemaVersion = source.SchemaVersion;
            PoseId = source.PoseId;
            DisplayName = source.DisplayName;
            HandSide = source.HandSide;
            JointLayoutVersion = source.JointLayoutVersion;
            NormalizationVersion = source.NormalizationVersion;
            MaxError = source.MaxError;
            m_Weights = System.Array.AsReadOnly(source.CopyJointWeights());
            var sourceSamples = source.SerializedSamples;
            var copies = new PoseSample[sourceSamples.Length];
            for (int i = 0; i < copies.Length; i++) copies[i] = sourceSamples[i].CloneForRuntime();
            m_Samples = System.Array.AsReadOnly(copies);
        }
    }
}
