using System;
using UnityEngine;

namespace HandPoseRecognition
{
    [Serializable]
    public struct JointPose
    {
        public HandJointId JointId;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool HasPosition;
        public bool HasRotation;
        public bool IsValid => HasPosition && HasRotation;

        public JointPose(HandJointId id, Vector3 position, Quaternion rotation, bool hasPosition, bool hasRotation)
        {
            JointId = id;
            Position = position;
            Rotation = rotation;
            HasPosition = hasPosition;
            HasRotation = hasRotation;
        }

        public static JointPose Unavailable(HandJointId id) => new JointPose(id, Vector3.zero, Quaternion.identity, false, false);

        internal JointPose Sanitized(bool tracked)
        {
            var result = Unavailable(JointId);
            result.HasPosition = tracked && HasPosition && PoseMath.IsFinite(Position);
            if (result.HasPosition) result.Position = Position;
            result.HasRotation = tracked && HasRotation && PoseMath.TryNormalize(Rotation, out result.Rotation);
            if (!result.HasRotation) result.Rotation = Quaternion.identity;
            return result;
        }
    }

    internal static class PoseMath
    {
        internal static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        internal static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        internal static bool IsFinite(Quaternion q) => IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);

        // Double intermediates avoid float overflow/underflow for finite quaternions.
        internal static bool TryNormalize(Quaternion q, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!IsFinite(q)) return false;
            double length = Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w);
            if (!IsFinite(length) || length == 0) return false;
            normalized = new Quaternion((float)(q.x / length), (float)(q.y / length), (float)(q.z / length), (float)(q.w / length));
            return IsFinite(normalized);
        }
    }
}
