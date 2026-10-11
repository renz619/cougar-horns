using UnityEngine;

namespace HandPoseRecognition.Tests
{
    // Synthetic geometry for contract tests only; not a validated ASL sample.
    public static class HandFrameFixtures
    {
        public static JointPose[] Joints()
        {
            var joints = HandFrame.CreateUnavailableJoints();
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new JointPose((HandJointId)i, new Vector3(i * .003f, .07f + i * .001f, .002f), Quaternion.identity, true, true);
            joints[0].Position = Vector3.zero;
            joints[7].Position = new Vector3(.04f, .05f, 0);
            joints[12].Position = new Vector3(0, .06f, 0);
            joints[22].Position = new Vector3(-.04f, .05f, 0);
            return joints;
        }

        public static HandFrame Raw(JointPose[] joints = null, HandSide side = HandSide.Right, bool tracked = true, ulong sequence = 1)
            => new HandFrame(2.5, sequence, side, tracked, HandFrameSpace.TrackingSpace, joints ?? Joints(),
                trackingSpaceToWorld: Quaternion.Euler(10, 20, 30), hasTrackingSpaceToWorld: true);
    }
}
