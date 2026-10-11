using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Unity-serializable owned canonical data, separate from runtime HandFrame ownership.</summary>
    [Serializable]
    public sealed class PoseSample
    {
        [SerializeField] int m_SchemaVersion;
        [SerializeField] HandSide m_HandSide;
        [SerializeField] HandFrameSpace m_Space;
        [SerializeField] int m_JointLayoutVersion;
        [SerializeField] int m_NormalizationVersion;
        [SerializeField] double m_SourceTimestampSeconds;
        [SerializeField] ulong m_SourceSequenceId;
        [SerializeField] JointPose[] m_Joints;
        public int SchemaVersion => m_SchemaVersion;
        public HandSide HandSide => m_HandSide;
        public HandFrameSpace Space => m_Space;
        public int JointLayoutVersion => m_JointLayoutVersion;
        public int NormalizationVersion => m_NormalizationVersion;
        public double SourceTimestampSeconds => m_SourceTimestampSeconds;
        public ulong SourceSequenceId => m_SourceSequenceId;
        internal JointPose[] SerializedJoints => m_Joints;
        public JointPose[] CopyJoints() => m_Joints == null ? null : (JointPose[])m_Joints.Clone();

        public bool TryGetJoint(HandJointId id, out JointPose joint)
        {
            int index = (int)id;
            joint = m_Joints != null && index >= 0 && index < m_Joints.Length
                ? m_Joints[index] : JointPose.Unavailable(HandJointId.Invalid);
            return joint.HasPosition;
        }

        public static bool TryCreate(HandFrame canonical, float[] weights, out PoseSample sample, out ValidationReport report)
        {
            sample = null;
            report = new ValidationReport();
            if (canonical == null || !canonical.IsValid || canonical.Space != HandFrameSpace.CanonicalHandSpace)
            {
                report.Add("Sample", "Requires a usable canonical frame produced by HandPoseNormalizer.");
                return false;
            }
            var candidate = new PoseSample
            {
                m_SchemaVersion = 1, m_HandSide = canonical.HandSide, m_Space = canonical.Space,
                m_JointLayoutVersion = canonical.JointLayoutVersion, m_NormalizationVersion = canonical.NormalizationVersion,
                m_SourceTimestampSeconds = canonical.TimestampSeconds, m_SourceSequenceId = canonical.SequenceId,
                m_Joints = canonical.CopyJoints()
            };
            PoseDataValidation.ValidateWeights(weights, "JointWeights", report);
            PoseDataValidation.ValidateSample(candidate, canonical.HandSide, 1, 1, weights, "Sample", report);
            if (!report.IsValid) return false;
            sample = candidate;
            return true;
        }

        internal PoseSample Clone() => new PoseSample
        {
            m_SchemaVersion = m_SchemaVersion, m_HandSide = m_HandSide, m_Space = m_Space,
            m_JointLayoutVersion = m_JointLayoutVersion, m_NormalizationVersion = m_NormalizationVersion,
            m_SourceTimestampSeconds = m_SourceTimestampSeconds, m_SourceSequenceId = m_SourceSequenceId,
            m_Joints = CopyJoints()
        };

        internal PoseSample CloneForRuntime()
        {
            var copy = Clone();
            for (int i = 0; i < copy.m_Joints.Length; i++) copy.m_Joints[i] = copy.m_Joints[i].Sanitized(true);
            return copy;
        }
    }
}
