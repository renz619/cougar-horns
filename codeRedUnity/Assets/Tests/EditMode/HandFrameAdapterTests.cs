using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;

namespace HandPoseRecognition.Tests
{
    // Real XR subsystem backed by deterministic synthetic provider storage.
    public class FrameTestProvider : XRHandSubsystemProvider
    {
        public static FrameTestProvider Current;
        public JointPose[] Joints = HandFrameFixtures.Joints();
        public XRHandSubsystem.UpdateSuccessFlags Flags = XRHandSubsystem.UpdateSuccessFlags.All;
        public FrameTestProvider() { Current = this; }
        public override void Start() { }
        public override void Stop() { }
        public override void Destroy() { Current = null; }
        public override void GetHandLayout(NativeArray<bool> layout)
        {
            for (int i = 0; i < layout.Length; i++) layout[i] = true;
        }
        public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(XRHandSubsystem.UpdateType type,
            ref Pose leftRoot, NativeArray<XRHandJoint> left, ref Pose rightRoot, NativeArray<XRHandJoint> right)
        {
            leftRoot = rightRoot = Pose.identity;
            for (int i = 0; i < Joints.Length; i++)
            {
                var j = Joints[i];
                var state = j.HasPosition ? XRHandJointTrackingState.Pose : XRHandJointTrackingState.None;
                var pose = new Pose(j.Position, j.HasRotation ? j.Rotation : default);
                left[i] = XRHandProviderUtility.CreateJoint(Handedness.Left, state, XRHandJointIDUtility.FromIndex(i), pose);
                right[i] = XRHandProviderUtility.CreateJoint(Handedness.Right, state, XRHandJointIDUtility.FromIndex(i), pose);
            }
            return Flags;
        }

        public static XRHandSubsystem CreateSubsystem()
        {
            const string id = "HandFrame-Contract-Tests";
            var descriptors = new List<XRHandSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            if (!descriptors.Exists(d => d.id == id))
                XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo { id = id, providerType = typeof(FrameTestProvider) });
            descriptors.Clear();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            return descriptors.Find(d => d.id == id).Create();
        }
    }

    public class HandFrameAdapterTests
    {
        XRHandSubsystem m_Subsystem;
        GameObject m_Owner;
        QuestHandAdapter m_Adapter;
        FrameTestProvider m_Provider;

        public static void Invoke(QuestHandAdapter adapter, string method) => typeof(QuestHandAdapter)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(adapter, null);

        [SetUp]
        public void SetUp()
        {
            m_Subsystem = FrameTestProvider.CreateSubsystem();
            m_Provider = FrameTestProvider.Current;
            m_Subsystem.Start();
            m_Owner = new GameObject("Synthetic adapter test");
            m_Adapter = m_Owner.AddComponent<QuestHandAdapter>();
            Invoke(m_Adapter, "Update");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Owner);
            m_Subsystem.Stop();
            m_Subsystem.Destroy();
        }

        [Test]
        public void DynamicPublishesOnce_BeforeRenderAndRepeatedReadsDoNotPublish()
        {
            Assert.IsFalse(m_Adapter.TryGetLatestFrame(out _));
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.BeforeRender);
            Assert.IsFalse(m_Adapter.TryGetLatestFrame(out _));
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            Assert.IsTrue(m_Adapter.TryGetLatestFrame(out var first));
            Assert.AreEqual(1ul, first.SequenceId);
            Assert.AreEqual(HandSide.Right, first.HandSide);
            Assert.IsTrue(first.IsTracked);
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.BeforeRender);
            m_Adapter.TryGetLatestFrame(out var same);
            Assert.AreSame(first, same);
            m_Provider.Joints[7].Position += Vector3.one;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var next);
            Assert.AreEqual(2ul, next.SequenceId);
            Assert.GreaterOrEqual(next.TimestampSeconds, first.TimestampSeconds);
            first.TryGetJoint(HandJointId.IndexProximal, out var retained);
            Assert.AreEqual(new Vector3(.04f, .05f, 0), retained.Position);
            for (int i = 0; i < 26; i++)
            {
                next.TryGetJoint((HandJointId)i, out var actual);
                Assert.AreEqual(m_Provider.Joints[i].Position, actual.Position, $"slot {i}");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void SideSpecificFlags_LossAndRecovery_ClearStaleData(bool left)
        {
            typeof(QuestHandAdapter).GetField("m_Handedness", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m_Adapter, left ? Handedness.Left : Handedness.Right);
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var tracked);
            Assert.AreEqual(left ? HandSide.Left : HandSide.Right, tracked.HandSide);
            m_Provider.Flags = left ? XRHandSubsystem.UpdateSuccessFlags.RightHandJoints | XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose
                : XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints | XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var lost);
            Assert.IsFalse(lost.IsTracked);
            Assert.AreEqual(tracked.SequenceId + 1, lost.SequenceId);
            Assert.IsFalse(m_Adapter.currentHand.isTracked);
            Assert.AreEqual(QuestHandAdapter.TrackingState.NotTracked, m_Adapter.trackingState);
            foreach (var joint in lost.CopyJoints()) Assert.IsFalse(joint.HasPosition || joint.HasRotation);
            m_Provider.Flags = XRHandSubsystem.UpdateSuccessFlags.All;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var recovered);
            Assert.IsTrue(recovered.IsTracked);
            Assert.AreEqual(lost.SequenceId + 1, recovered.SequenceId);
            Assert.IsTrue(tracked.IsTracked);
        }

        [Test]
        public void SelectedHandRootOrJointFailure_DoesNotPublishGeometry()
        {
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Provider.Flags = XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var noJoints);
            Assert.IsFalse(noJoints.IsTracked);
            m_Provider.Flags = XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var noRoot);
            Assert.IsFalse(noRoot.IsTracked);
        }

        [Test]
        public void ShutdownAndDisable_PublishStatusOnce_AndRetainedFrameSurvives()
        {
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var retained);
            m_Subsystem.Stop();
            m_Adapter.TryGetLatestFrame(out var stopped);
            Assert.IsFalse(stopped.IsTracked);
            Assert.AreEqual(retained.SequenceId + 1, stopped.SequenceId);
            Invoke(m_Adapter, "Update");
            m_Adapter.TryGetLatestFrame(out var same);
            Assert.AreSame(stopped, same);
            m_Subsystem.Start();
            Invoke(m_Adapter, "Update");
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.enabled = false;
            m_Adapter.TryGetLatestFrame(out var disabled);
            Assert.IsFalse(disabled.IsTracked);
            m_Adapter.TryGetLatestFrame(out same);
            Assert.AreSame(disabled, same);
            Assert.IsTrue(retained.IsTracked);
            Assert.IsFalse(m_Adapter.currentHand.isTracked);
        }

        [Test]
        public void MissingAnchors_DoNotChangeLegacyValidity_OneMissingDigitJointDoes()
        {
            m_Provider.Joints[0].HasPosition = m_Provider.Joints[1].HasPosition = false;
            m_Provider.Joints[3].HasRotation = false;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var frame);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.Wrist, out _));
            Assert.IsTrue(frame.TryGetJoint(HandJointId.ThumbProximal, out var thumb));
            Assert.IsFalse(thumb.HasRotation);
            Assert.AreEqual(QuestHandAdapter.TrackingState.Tracked, m_Adapter.trackingState);
            var legacy = m_Adapter.currentHand;
            Assert.IsTrue(legacy.index.isValid && legacy.middle.isValid && legacy.ring.isValid && legacy.little.isValid && legacy.thumb.isValid);
            Assert.AreEqual(m_Provider.Joints[10].Position, legacy.index.tip);
            Assert.AreEqual(m_Provider.Joints[25].Position, legacy.little.tip);
            m_Provider.Joints[9].HasPosition = false;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out frame);
            Assert.IsTrue(frame.TryGetJoint(HandJointId.IndexTip, out _));
            Assert.IsFalse(frame.TryGetJoint(HandJointId.IndexDistal, out var failed));
            Assert.IsFalse(failed.HasRotation); // failed XR read must not invent identity rotation
            Assert.IsFalse(m_Adapter.currentHand.index.isValid);
            Assert.IsTrue(m_Adapter.currentHand.middle.isValid);
            Assert.AreEqual(QuestHandAdapter.TrackingState.IncompleteJoints, m_Adapter.trackingState);
        }

        [Test]
        public void NonfiniteProviderPose_IsUnavailable_AndOrientationMetadataIsCopiedOnce()
        {
            m_Owner.transform.rotation = Quaternion.Euler(0, 0, 90);
            typeof(QuestHandAdapter).GetField("m_TrackingSpace", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m_Adapter, m_Owner.transform);
            m_Provider.Joints[9].Position = new Vector3(float.NaN, 0, 0);
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var frame);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.IndexDistal, out _));
            Assert.IsFalse(m_Adapter.currentHand.index.isValid);
            Assert.Less(Quaternion.Angle(m_Owner.transform.rotation, frame.TrackingSpaceToWorld), .001f);
            Assert.Less(Quaternion.Angle(frame.TrackingSpaceToWorld, m_Adapter.currentHand.trackingSpaceToWorld), .001f);
            frame.TryGetJoint(HandJointId.ThumbTip, out var tip);
            Assert.AreEqual(m_Provider.Joints[5].Position, tip.Position);
        }

        static void SetFinger(JointPose[] joints, int start, FingerJoints finger)
        {
            Vector3[] positions = { finger.metacarpal, finger.proximal, finger.intermediate, finger.distal, finger.tip };
            for (int i = 0; i < positions.Length; i++)
                joints[start + i] = new JointPose((HandJointId)(start + i), positions[i], Quaternion.identity, finger.isValid, false);
        }

        [TestCase(HandPose.Open)] [TestCase(HandPose.Fist)] [TestCase(HandPose.Pointing)]
        [TestCase(HandPose.V)] [TestCase(HandPose.U)] [TestCase(HandPose.Y)]
        [TestCase(HandPose.L)] [TestCase(HandPose.ThumbsUp)]
        public void LegacyPoses_SurviveAcquisitionWithoutWristAndPalm(HandPose expected)
        {
            var hand = FakeHandData.Hand(expected == HandPose.Open ? 0 : 80);
            if (expected == HandPose.Pointing || expected == HandPose.L)
                hand = FakeHandData.SplayedHand(0, 80, 80, 80, 0, 0, 0, 0);
            if (expected == HandPose.V || expected == HandPose.U)
                hand = FakeHandData.SplayedHand(0, 0, 80, 80, expected == HandPose.V ? -10 : 0, expected == HandPose.V ? 10 : 0, 0, 0);
            if (expected == HandPose.Y) hand = FakeHandData.SplayedHand(80, 80, 80, 0, 0, 0, 0, 0);
            hand.thumb = expected == HandPose.L || expected == HandPose.Y || expected == HandPose.ThumbsUp
                ? FakeHandData.Thumb(45, 0) : FakeHandData.Thumb(0, 60);
            if (expected == HandPose.ThumbsUp)
            {
                m_Owner.transform.rotation = Quaternion.FromToRotation(hand.thumb.tip - hand.thumb.proximal, Vector3.up);
                typeof(QuestHandAdapter).GetField("m_TrackingSpace", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(m_Adapter, m_Owner.transform);
                hand = QuestHandAdapter.ApplyTrackingSpace(hand, m_Owner.transform);
            }
            var settings = new HandPoseClassifierSettings();
            Assert.AreEqual(expected, HandPoseClassifier.ClassifyPoseWithOrientation(hand, settings));
            var joints = HandFrame.CreateUnavailableJoints();
            SetFinger(joints, 6, hand.index);
            SetFinger(joints, 11, hand.middle);
            SetFinger(joints, 16, hand.ring);
            SetFinger(joints, 21, hand.little);
            Vector3[] thumb = { hand.thumb.metacarpal, hand.thumb.proximal, hand.thumb.distal, hand.thumb.tip };
            for (int i = 0; i < thumb.Length; i++) joints[i + 2] = new JointPose((HandJointId)(i + 2), thumb[i], Quaternion.identity, true, false);
            m_Provider.Joints = joints;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            Assert.AreEqual(expected, HandPoseClassifier.ClassifyPoseWithOrientation(m_Adapter.currentHand, settings));
            m_Adapter.TryGetLatestFrame(out var frame);
            Assert.IsFalse(frame.TryGetJoint(HandJointId.Wrist, out _));
            Assert.AreEqual(m_Adapter.currentHand.thumb.tip, HandFrameLegacyProjection.Project(frame).thumb.tip);
        }

        [Test]
        public void WholeDigitUnavailable_DoesNotEraseOtherDigits()
        {
            for (int i = 2; i <= 5; i++) m_Provider.Joints[i].HasPosition = false;
            m_Subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            m_Adapter.TryGetLatestFrame(out var frame);
            Assert.IsFalse(m_Adapter.currentHand.thumb.isValid);
            Assert.IsTrue(m_Adapter.currentHand.index.isValid);
            for (int i = 2; i <= 5; i++) Assert.IsFalse(frame.TryGetJoint((HandJointId)i, out _));
            Assert.AreEqual(QuestHandAdapter.TrackingState.Tracked, m_Adapter.trackingState);
        }
    }
}
