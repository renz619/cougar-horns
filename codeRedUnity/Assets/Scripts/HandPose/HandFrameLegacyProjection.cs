namespace HandPoseRecognition
{
    /// <summary>Projects positions from one acquisition, without requiring wrist/palm or rotations.</summary>
    public static class HandFrameLegacyProjection
    {
        public static HandJointData Project(HandFrame frame)
        {
            if (frame == null || !frame.IsTracked) return default;
            bool thumbValid = frame.TryGetJoint(HandJointId.ThumbMetacarpal, out var a)
                & frame.TryGetJoint(HandJointId.ThumbProximal, out var b)
                & frame.TryGetJoint(HandJointId.ThumbDistal, out var c)
                & frame.TryGetJoint(HandJointId.ThumbTip, out var d);
            return new HandJointData
            {
                isTracked = true,
                thumb = new ThumbJoints { metacarpal = a.Position, proximal = b.Position, distal = c.Position, tip = d.Position, isValid = thumbValid },
                index = Finger(frame, 6), middle = Finger(frame, 11), ring = Finger(frame, 16), little = Finger(frame, 21),
                trackingSpaceToWorld = frame.TrackingSpaceToWorld,
                hasTrackingSpaceToWorld = frame.HasTrackingSpaceToWorld
            };
        }

        internal static HandJointData Project(JointPose[] joints, bool tracked)
        {
            if (!tracked) return default;
            return new HandJointData { isTracked = true, thumb = Thumb(joints, 2),
                index = Finger(joints, 6), middle = Finger(joints, 11), ring = Finger(joints, 16), little = Finger(joints, 21) };
        }

        static ThumbJoints Thumb(JointPose[] j, int i) => new ThumbJoints
        {
            metacarpal = j[i].Position, proximal = j[i + 1].Position, distal = j[i + 2].Position, tip = j[i + 3].Position,
            isValid = j[i].HasPosition && j[i + 1].HasPosition && j[i + 2].HasPosition && j[i + 3].HasPosition
        };

        static FingerJoints Finger(JointPose[] j, int i) => new FingerJoints
        {
            metacarpal = j[i].Position, proximal = j[i + 1].Position, intermediate = j[i + 2].Position,
            distal = j[i + 3].Position, tip = j[i + 4].Position,
            isValid = j[i].HasPosition && j[i + 1].HasPosition && j[i + 2].HasPosition && j[i + 3].HasPosition && j[i + 4].HasPosition
        };

        static FingerJoints Finger(HandFrame f, int i)
        {
            bool valid = f.TryGetJoint((HandJointId)i, out var a) & f.TryGetJoint((HandJointId)(i + 1), out var b)
                & f.TryGetJoint((HandJointId)(i + 2), out var c) & f.TryGetJoint((HandJointId)(i + 3), out var d)
                & f.TryGetJoint((HandJointId)(i + 4), out var e);
            return new FingerJoints { metacarpal = a.Position, proximal = b.Position, intermediate = c.Position,
                distal = d.Position, tip = e.Position, isValid = valid };
        }
    }
}
