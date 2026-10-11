using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Pure, one-time tracking-to-canonical conversion. Version 1 never mirrors hands.</summary>
    public static class HandPoseNormalizer
    {
        // Physical anchor tolerances in meters. Changing the coordinate algorithm requires a version bump.
        public const float MinimumPalmWidthMeters = 1e-4f;
        public const float MinimumForwardLengthMeters = 1e-4f;
        public const float MinimumCrossDirectionLength = 1e-6f;

        public static bool TryNormalizeFrame(HandFrame raw, out HandFrame normalized, out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (raw == null || !raw.IsValid) return Fail("Frame is null, untracked, unknown-side/space, or has no usable positions.", out failureReason);
            if (raw.Space != HandFrameSpace.TrackingSpace || raw.JointLayoutVersion != 1 || raw.NormalizationVersion != 0)
                return Fail("Requires TrackingSpace/layout 1/normalization 0; do not normalize canonical frames twice.", out failureReason);
            if (!raw.TryGetJoint(HandJointId.Wrist, out var wrist)) return Fail("Missing anchor Wrist.", out failureReason);
            if (!raw.TryGetJoint(HandJointId.IndexProximal, out var index)) return Fail("Missing anchor IndexProximal.", out failureReason);
            if (!raw.TryGetJoint(HandJointId.MiddleProximal, out var middle)) return Fail("Missing anchor MiddleProximal.", out failureReason);
            if (!raw.TryGetJoint(HandJointId.LittleProximal, out var little)) return Fail("Missing anchor LittleProximal.", out failureReason);

            var across = index.Position - little.Position;
            float scale = across.magnitude;
            if (!PoseMath.IsFinite(across) || !PoseMath.IsFinite(scale) || scale < MinimumPalmWidthMeters)
                return Fail("Degenerate/nonfinite palm width (minimum 1e-4 meters).", out failureReason);
            var x = across / scale;
            var forward = middle.Position - wrist.Position;
            var projected = forward - Vector3.Dot(forward, x) * x;
            float length = projected.magnitude;
            if (!PoseMath.IsFinite(forward) || !PoseMath.IsFinite(projected) || !PoseMath.IsFinite(length) || length < MinimumForwardLengthMeters)
                return Fail("Degenerate/nonfinite projected forward anchor (minimum 1e-4 meters).", out failureReason);
            var y = projected / length;
            var cross = Vector3.Cross(x, y);
            float crossLength = cross.magnitude;
            if (!PoseMath.IsFinite(x) || !PoseMath.IsFinite(y) || !PoseMath.IsFinite(crossLength) || crossLength < MinimumCrossDirectionLength)
                return Fail("Degenerate/nonfinite basis cross direction.", out failureReason);
            var z = cross / crossLength;
            if (!PoseMath.TryNormalize(Quaternion.LookRotation(z, y), out var basis))
                return Fail("Invalid basis rotation.", out failureReason);
            var inverse = Quaternion.Inverse(basis);
            var joints = raw.CopyJoints();
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (joint.HasPosition)
                {
                    var offset = joint.Position - wrist.Position;
                    var position = new Vector3(Vector3.Dot(offset, x), Vector3.Dot(offset, y), Vector3.Dot(offset, z)) / scale;
                    if (!PoseMath.IsFinite(offset) || !PoseMath.IsFinite(position))
                        return Fail($"Joints[{i}]/{joint.JointId}: canonical position overflow/nonfinite result.", out failureReason);
                    joint.Position = position;
                }
                if (joint.HasRotation)
                {
                    joint.HasRotation = PoseMath.TryNormalize(inverse * joint.Rotation, out joint.Rotation);
                    if (!joint.HasRotation) joint.Rotation = Quaternion.identity;
                }
                joints[i] = joint;
            }
            normalized = new HandFrame(raw.TimestampSeconds, raw.SequenceId, raw.HandSide, raw.IsTracked,
                HandFrameSpace.CanonicalHandSpace, joints, raw.JointLayoutVersion, 1,
                raw.TrackingSpaceToWorld, raw.HasTrackingSpaceToWorld);
            return true;
        }

        static bool Fail(string reason, out string failureReason) { failureReason = reason; return false; }
    }
}
