using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class HandPoseNormalizerTests
    {
        [Test]
        public void KnownCoordinates_MetadataAndSource_ArePreserved()
        {
            var raw = HandFrameFixtures.Raw();
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(raw, out var canonical, out var reason), reason);
            canonical.TryGetJoint(HandJointId.Wrist, out var wrist);
            canonical.TryGetJoint(HandJointId.IndexProximal, out var index);
            canonical.TryGetJoint(HandJointId.LittleProximal, out var little);
            Assert.AreEqual(Vector3.zero, wrist.Position);
            Assert.Less(Vector3.Distance(new Vector3(.5f, .625f, 0), index.Position), 1e-5f);
            Assert.Less(Vector3.Distance(new Vector3(-.5f, .625f, 0), little.Position), 1e-5f);
            Assert.AreEqual(1, canonical.NormalizationVersion);
            Assert.AreEqual(HandFrameSpace.CanonicalHandSpace, canonical.Space);
            Assert.AreEqual(raw.SequenceId, canonical.SequenceId);
            Assert.AreEqual(raw.TimestampSeconds, canonical.TimestampSeconds);
            Assert.AreEqual(raw.HandSide, canonical.HandSide);
            Assert.AreEqual(raw.TrackingSpaceToWorld, canonical.TrackingSpaceToWorld);
            Assert.IsTrue(canonical.HasTrackingSpaceToWorld);
            raw.TryGetJoint(HandJointId.IndexProximal, out index);
            Assert.AreEqual(new Vector3(.04f, .05f, 0), index.Position);
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(canonical, out var twice, out reason));
            Assert.IsNull(twice);
            StringAssert.Contains("twice", reason);
        }

        [TestCase(HandSide.Left, .4f)] [TestCase(HandSide.Right, 3f)]
        public void TranslationRotationPositiveScale_LeaveShapeAndConvertedRotationsInvariant(HandSide side, float scale)
        {
            var joints = HandFrameFixtures.Joints();
            var rotation = Quaternion.Euler(31, -52, 17);
            var moved = (JointPose[])joints.Clone();
            for (int i = 0; i < moved.Length; i++)
            {
                moved[i].Position = rotation * (moved[i].Position * scale) + new Vector3(2, -1, 4);
                moved[i].Rotation = rotation * moved[i].Rotation;
            }
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints, side), out var a, out var reason), reason);
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(moved, side), out var b, out reason), reason);
            for (int i = 0; i < 26; i++)
            {
                a.TryGetJoint((HandJointId)i, out var expected);
                b.TryGetJoint((HandJointId)i, out var actual);
                Assert.Less(Vector3.Distance(expected.Position, actual.Position), 2e-4f, $"joint {i}");
                Assert.Less(Quaternion.Angle(expected.Rotation, actual.Rotation), .05f);
            }
            Assert.AreEqual(side, b.HandSide);
        }

        [Test]
        public void PartialNonAnchorsAndRotationOnlyJoints_RemainExplicit()
        {
            var joints = HandFrameFixtures.Joints();
            joints[10].HasPosition = joints[10].HasRotation = false;
            joints[15].HasPosition = false;
            joints[20].HasRotation = false;
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints), out var frame, out var reason), reason);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.IndexTip, out var absent));
            Assert.IsFalse(absent.HasRotation);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.MiddleTip, out var rotationOnly));
            Assert.IsTrue(rotationOnly.HasRotation);
            Assert.IsTrue(frame.TryGetJoint(HandJointId.RingTip, out var positionOnly));
            Assert.IsFalse(positionOnly.HasRotation);
        }

        [TestCase(0)] [TestCase(7)] [TestCase(12)] [TestCase(22)]
        public void EachMissingAnchor_RejectsWithJointName(int slot)
        {
            var joints = HandFrameFixtures.Joints();
            joints[slot].HasPosition = false;
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints), out var result, out var reason));
            Assert.IsNull(result);
            StringAssert.Contains(((HandJointId)slot).ToString(), reason);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void DegeneracyAndOverflow_FailSafely(int kind)
        {
            var joints = HandFrameFixtures.Joints();
            if (kind == 0) joints[7].Position = joints[22].Position;
            if (kind == 1) joints[12].Position = new Vector3(.06f, 0, 0);
            if (kind == 2)
            {
                joints[7].Position = new Vector3(float.MaxValue, 0, 0);
                joints[22].Position = new Vector3(-float.MaxValue, 0, 0);
            }
            if (kind == 3) joints[25].Position = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            if (kind == 4) joints[7].Position = joints[22].Position + new Vector3(.00001f, 0, 0);
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints), out var result, out var reason));
            Assert.IsNull(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void NullUntrackedUnknownSide_Reject()
        {
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(null, out _, out _));
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(tracked: false), out _, out _));
            Assert.IsFalse(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(side: HandSide.Unknown), out _, out _));
        }

        [Test]
        public void LeftHands_AreNotMirroredIntoRightTemplates()
        {
            var rightJoints = HandFrameFixtures.Joints();
            var leftJoints = (JointPose[])rightJoints.Clone();
            for (int i = 0; i < 26; i++) leftJoints[i].Position.x = -leftJoints[i].Position.x;
            HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(rightJoints), out var right, out _);
            HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(leftJoints, HandSide.Left), out var left, out _);
            right.TryGetJoint(HandJointId.IndexTip, out var r);
            left.TryGetJoint(HandJointId.IndexTip, out var l);
            Assert.AreEqual(r.Position.x, l.Position.x, 1e-5);
            Assert.AreEqual(-r.Position.z, l.Position.z, 1e-5);
            Assert.Greater(Mathf.Abs(r.Position.z), 0);
        }
    }
}
