using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>An owned immutable snapshot. Tracking positions are meters; canonical positions are unitless.</summary>
    public sealed class HandFrame
    {
        public const int JointCount = 26;
        public const int SupportedJointLayoutVersion = 1;
        public const int SupportedNormalizationVersion = 1;
        readonly JointPose[] m_Joints;
        public double TimestampSeconds { get; }
        public ulong SequenceId { get; }
        public HandSide HandSide { get; }
        public bool IsTracked { get; }
        public HandFrameSpace Space { get; }
        public int JointLayoutVersion { get; }
        public int NormalizationVersion { get; }
        // Always tracking-space metadata, including on canonical frames. NOT canonical-to-world.
        public Quaternion TrackingSpaceToWorld { get; }
        public bool HasTrackingSpaceToWorld { get; }
        public bool IsValid { get; }

        public HandFrame(double timestampSeconds, ulong sequenceId, HandSide handSide, bool isTracked,
            HandFrameSpace space, JointPose[] joints, int jointLayoutVersion = 1, int normalizationVersion = 0,
            Quaternion trackingSpaceToWorld = default, bool hasTrackingSpaceToWorld = false)
        {
            if (!PoseMath.IsFinite(timestampSeconds) || timestampSeconds < 0)
                throw new ArgumentException("TimestampSeconds must be finite, nonnegative session time.");
            if (handSide != HandSide.Unknown && handSide != HandSide.Left && handSide != HandSide.Right)
                throw new ArgumentException("Unsupported HandSide.");
            if (jointLayoutVersion != SupportedJointLayoutVersion)
                throw new ArgumentException("Unsupported JointLayoutVersion.");
            if (!((space == HandFrameSpace.TrackingSpace && normalizationVersion == 0)
                || (space == HandFrameSpace.CanonicalHandSpace && normalizationVersion == SupportedNormalizationVersion)
                || (space == HandFrameSpace.Unknown && normalizationVersion == 0)))
                throw new ArgumentException("Unsupported Space/NormalizationVersion combination.");
            if (joints == null || joints.Length != JointCount)
                throw new ArgumentException("Joints must contain exactly 26 ordered slots.");
            if (!PoseMath.IsFinite(trackingSpaceToWorld))
                throw new ArgumentException("TrackingSpaceToWorld must be finite, even when unavailable.");
            var worldRotation = Quaternion.identity;
            if (hasTrackingSpaceToWorld && !PoseMath.TryNormalize(trackingSpaceToWorld, out worldRotation))
                throw new ArgumentException("Available TrackingSpaceToWorld must be a nonzero quaternion.");

            m_Joints = new JointPose[JointCount];
            bool anyPosition = false;
            for (int i = 0; i < JointCount; i++)
            {
                if (joints[i].JointId != (HandJointId)i)
                    throw new ArgumentException($"Joints[{i}] must have ID {(HandJointId)i}; duplicate/misordered ID.");
                m_Joints[i] = joints[i].Sanitized(isTracked);
                anyPosition |= m_Joints[i].HasPosition;
            }
            TimestampSeconds = timestampSeconds;
            SequenceId = sequenceId;
            HandSide = handSide;
            IsTracked = isTracked;
            Space = space;
            JointLayoutVersion = jointLayoutVersion;
            NormalizationVersion = normalizationVersion;
            HasTrackingSpaceToWorld = hasTrackingSpaceToWorld;
            TrackingSpaceToWorld = worldRotation;
            IsValid = isTracked && handSide != HandSide.Unknown && space != HandFrameSpace.Unknown && anyPosition;
        }

        /// <summary>Returns position availability; inspect HasRotation separately. Invalid IDs return an unavailable Invalid joint.</summary>
        public bool TryGetJoint(HandJointId id, out JointPose joint)
        {
            int index = (int)id;
            joint = index >= 0 && index < JointCount ? m_Joints[index] : JointPose.Unavailable(HandJointId.Invalid);
            return joint.HasPosition;
        }

        public JointPose[] CopyJoints() => (JointPose[])m_Joints.Clone();
        public static JointPose[] CreateUnavailableJoints()
        {
            var joints = new JointPose[JointCount];
            for (int i = 0; i < joints.Length; i++) joints[i] = JointPose.Unavailable((HandJointId)i);
            return joints;
        }
    }
}
