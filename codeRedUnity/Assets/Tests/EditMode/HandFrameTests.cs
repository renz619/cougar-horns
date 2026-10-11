using System;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class HandFrameTests
    {
        [Test]
        public void FrozenLayout_AndUnavailableSlots_AreExplicit()
        {
            string[] names = { "Wrist", "Palm", "ThumbMetacarpal", "ThumbProximal", "ThumbDistal", "ThumbTip",
                "IndexMetacarpal", "IndexProximal", "IndexIntermediate", "IndexDistal", "IndexTip",
                "MiddleMetacarpal", "MiddleProximal", "MiddleIntermediate", "MiddleDistal", "MiddleTip",
                "RingMetacarpal", "RingProximal", "RingIntermediate", "RingDistal", "RingTip",
                "LittleMetacarpal", "LittleProximal", "LittleIntermediate", "LittleDistal", "LittleTip" };
            var joints = HandFrame.CreateUnavailableJoints();
            Assert.AreEqual(27, Enum.GetValues(typeof(HandJointId)).Length);
            for (int i = 0; i < 26; i++)
            {
                Assert.AreEqual(names[i], ((HandJointId)i).ToString());
                Assert.AreEqual((HandJointId)i, joints[i].JointId);
                Assert.IsFalse(joints[i].HasPosition);
                Assert.IsFalse(joints[i].HasRotation);
            }
            Assert.IsFalse(default(JointPose).IsValid);
            Assert.AreEqual(0, (int)HandSide.Unknown);
            Assert.AreEqual(1, (int)HandSide.Left);
            Assert.AreEqual(2, (int)HandSide.Right);
            Assert.AreEqual(0, (int)HandFrameSpace.Unknown);
        }

        [TestCase(-1)] [TestCase(26)] [TestCase(100)]
        public void InvalidJointIds_AreUnavailable(int id)
        {
            Assert.IsFalse(HandFrameFixtures.Raw().TryGetJoint((HandJointId)id, out var joint));
            Assert.AreEqual(HandJointId.Invalid, joint.JointId);
        }

        [Test]
        public void ConstructionOwnsInput_AndReturnedCopies()
        {
            var joints = HandFrameFixtures.Joints();
            var frame = HandFrameFixtures.Raw(joints);
            var copy = frame.CopyJoints();
            joints[7].Position = Vector3.one * 99;
            copy[7].Position = Vector3.one * -99;
            Assert.IsTrue(frame.TryGetJoint(HandJointId.IndexProximal, out var joint));
            Assert.AreEqual(new Vector3(.04f, .05f, 0), joint.Position);
        }

        [Test]
        public void MalformedStructure_AndMetadata_ThrowExplicitly()
        {
            Assert.Throws<ArgumentException>(() => HandFrameFixtures.Raw(new JointPose[25]));
            Assert.Throws<ArgumentException>(() => HandFrameFixtures.Raw(new JointPose[26]));
            var joints = HandFrameFixtures.Joints();
            joints[8].JointId = HandJointId.IndexProximal;
            Assert.Throws<ArgumentException>(() => HandFrameFixtures.Raw(joints));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints(), 2));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints(), 1, 1));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.CanonicalHandSpace, HandFrameFixtures.Joints(), 1, 0));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, (HandSide)4, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints()));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints(), trackingSpaceToWorld: new Quaternion(float.NaN, 0, 0, 1)));
            Assert.Throws<ArgumentException>(() => new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints(), hasTrackingSpaceToWorld: true));
        }

        [TestCase(-1d)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidTimestamp_IsRejected(double time)
        {
            Assert.Throws<ArgumentException>(() => new HandFrame(time, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, HandFrameFixtures.Joints()));
        }

        [Test]
        public void PositionAndRotationValidity_AreIndependent_OriginIsValid()
        {
            var joints = HandFrameFixtures.Joints();
            joints[0].Rotation = default;
            joints[1].Position = new Vector3(float.NaN, 0, 0);
            joints[2].Rotation = new Quaternion(0, 0, float.PositiveInfinity, 1);
            joints[3].Rotation = new Quaternion(0, 0, 0, 4);
            joints[4].HasRotation = false;
            joints[5].Position = new Vector3(0, float.NegativeInfinity, 0);
            var frame = HandFrameFixtures.Raw(joints);
            Assert.IsTrue(frame.IsValid);
            Assert.IsTrue(frame.TryGetJoint(HandJointId.Wrist, out var wrist));
            Assert.AreEqual(Vector3.zero, wrist.Position);
            Assert.IsFalse(wrist.HasRotation);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.Palm, out var palm));
            Assert.AreEqual(Vector3.zero, palm.Position);
            Assert.IsTrue(palm.HasRotation);
            frame.TryGetJoint(HandJointId.ThumbMetacarpal, out var thumb);
            Assert.IsFalse(thumb.HasRotation);
            frame.TryGetJoint(HandJointId.ThumbProximal, out thumb);
            Assert.AreEqual(Quaternion.identity, thumb.Rotation);
            frame.TryGetJoint(HandJointId.ThumbDistal, out thumb);
            Assert.IsFalse(thumb.HasRotation);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.ThumbTip, out _));
        }

        [Test]
        public void UnknownOrUntrackedOrEmpty_IsUnusable()
        {
            Assert.IsFalse(HandFrameFixtures.Raw(side: HandSide.Unknown).IsValid);
            Assert.IsFalse(new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.Unknown, HandFrameFixtures.Joints()).IsValid);
            Assert.IsFalse(HandFrameFixtures.Raw(HandFrame.CreateUnavailableJoints()).IsValid);
            var lost = HandFrameFixtures.Raw(tracked: false);
            Assert.IsFalse(lost.IsValid);
            foreach (var joint in lost.CopyJoints())
            {
                Assert.IsFalse(joint.HasPosition);
                Assert.IsFalse(joint.HasRotation);
            }
        }
    }
}
