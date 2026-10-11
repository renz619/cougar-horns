using UnityEngine;

namespace HandPoseRecognition
{
    [CreateAssetMenu(fileName = "HandPoseTemplate", menuName = "Hand Pose/Template")]
    public sealed class HandPoseData : ScriptableObject
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] string m_PoseId;
        [SerializeField] string m_DisplayName;
        [SerializeField] HandSide m_HandSide;
        [SerializeField] int m_JointLayoutVersion = 1;
        [SerializeField] int m_NormalizationVersion = 1;
        [SerializeField] PoseSample[] m_Samples = new PoseSample[0];
        [SerializeField] float[] m_JointWeights = CreateDefaultWeights();
        [SerializeField, Tooltip("Inclusive unitless palm-width distance threshold; provisional, uncalibrated.")] float m_MaxError = .1f;
        [SerializeField, Min(1), Tooltip("Storage budget, not an empirically optimal sample count.")] int m_MaxSamplesPerPose = 32;
        public int SchemaVersion => m_SchemaVersion;
        public string PoseId => m_PoseId;
        public string DisplayName => m_DisplayName;
        public HandSide HandSide => m_HandSide;
        public int JointLayoutVersion => m_JointLayoutVersion;
        public int NormalizationVersion => m_NormalizationVersion;
        public float MaxError => m_MaxError;
        public int MaxSamplesPerPose => m_MaxSamplesPerPose;
        public int SampleCount => m_Samples == null ? 0 : m_Samples.Length;
        internal PoseSample[] SerializedSamples => m_Samples;
        internal float[] SerializedWeights => m_JointWeights;
        public float[] CopyJointWeights() => m_JointWeights == null ? null : (float[])m_JointWeights.Clone();
        public PoseSample[] CopySamples() => CloneSamples(m_Samples);

        /// <summary>Copies authoring input. Explicit validation/build remains mandatory, including Inspector edits.</summary>
        public void SetDefinition(string poseId, string displayName, HandSide side, PoseSample[] samples,
            float[] weights, float maxError, int maxSamplesPerPose = 32)
        {
            m_PoseId = poseId;
            m_DisplayName = displayName;
            m_HandSide = side;
            m_Samples = CloneSamples(samples);
            m_JointWeights = weights == null ? null : (float[])weights.Clone();
            m_MaxError = maxError;
            m_MaxSamplesPerPose = maxSamplesPerPose;
        }

        public bool TryValidate(out ValidationReport report)
        {
            report = new ValidationReport();
            PoseDataValidation.ValidateTemplate(this, $"Template[{name}]/Pose[{PoseId}]", report);
            return report.IsValid;
        }

        public static float[] CreateDefaultWeights()
        {
            var weights = new float[HandFrame.JointCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = 1;
            return weights;
        }

        static PoseSample[] CloneSamples(PoseSample[] samples)
        {
            if (samples == null) return null;
            var owned = new PoseSample[samples.Length];
            for (int i = 0; i < owned.Length; i++) owned[i] = samples[i]?.Clone();
            return owned;
        }
    }
}
