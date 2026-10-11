using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public static class RecognitionFixtures
    {
        public static HandFrame Raw(double time = 1, ulong sequence = 1, bool tracked = true,
            HandSide side = HandSide.Right, JointPose[] joints = null)
            => new HandFrame(time, sequence, side, tracked, HandFrameSpace.TrackingSpace, joints ?? HandFrameFixtures.Joints());
        public static HandFrame Canonical(double time = 1, ulong sequence = 1, JointPose[] joints = null, HandSide side = HandSide.Right)
        {
            if (joints != null) return new HandFrame(time, sequence, side, true, HandFrameSpace.CanonicalHandSpace, joints, normalizationVersion: 1);
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(Raw(time, sequence, side: side), out var canonical, out var reason), reason);
            return canonical;
        }
        public static float[] Weights(params int[] slots)
        {
            var weights = new float[26];
            foreach (int i in slots) weights[i] = 1;
            return weights;
        }
        public static PoseSample Sample(HandFrame frame, float[] weights)
        {
            Assert.IsTrue(PoseSample.TryCreate(frame, weights, out var sample, out var report), report.ToString());
            return sample;
        }
    }

    public class ShapeRecognizerTests
    {
        readonly List<UnityEngine.Object> m_Owned = new List<UnityEngine.Object>();
        HandPoseData Pose(string id, HandFrame frame, float[] weights, float threshold = .1f, params HandFrame[] more)
        {
            var asset = ScriptableObject.CreateInstance<HandPoseData>(); m_Owned.Add(asset);
            var samples = new PoseSample[1 + more.Length]; samples[0] = RecognitionFixtures.Sample(frame, weights);
            for (int i = 0; i < more.Length; i++) samples[i + 1] = RecognitionFixtures.Sample(more[i], weights);
            asset.SetDefinition(id, "Display " + id, frame.HandSide, samples, weights, threshold);
            return asset;
        }
        PoseDatabaseSnapshot Catalog(params HandPoseData[] poses)
        {
            var db = ScriptableObject.CreateInstance<PoseDatabase>(); m_Owned.Add(db); db.SetTemplates(poses);
            Assert.IsTrue(db.TryBuildSnapshot(out var snapshot, out var report), report.ToString()); return snapshot;
        }
        [TearDown] public void Cleanup() { foreach (var item in m_Owned) UnityEngine.Object.DestroyImmediate(item); m_Owned.Clear(); }
        static HandFrame Offset(HandFrame frame, int slot, Vector3 delta)
        { var joints = frame.CopyJoints(); joints[slot].Position += delta; return RecognitionFixtures.Canonical(joints: joints); }

        [Test] public void ExactMatch_ReportsIdScoreAndProvenance()
        {
            var q = RecognitionFixtures.Canonical(); var catalog = Catalog(Pose("shape", q, HandPoseData.CreateDefaultWeights()));
            var result = ShapeRecognizer.Recognize(q, catalog, 1, .25);
            Assert.AreEqual(RecognitionStatus.Matched, result.Status); Assert.IsTrue(result.IsMatch); Assert.IsTrue(result.HasScore);
            Assert.AreEqual("shape", result.PoseId); Assert.AreEqual(0, result.Error); Assert.AreEqual(0, result.SampleIndex);
            Assert.AreEqual(q.SequenceId, result.SequenceId); Assert.AreEqual(q.TimestampSeconds, result.TimestampSeconds);
        }
        [Test] public void WeightedEuclidean_HandComputed_NotSquared()
        {
            var p = RecognitionFixtures.Canonical(); var weights = RecognitionFixtures.Weights(0, 1); weights[1] = 3;
            var joints = p.CopyJoints(); joints[0].Position += new Vector3(3, 4, 0); joints[1].Position += new Vector3(0, 0, 2);
            var result = ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(joints: joints), Catalog(Pose("weighted", p, weights, 3)), 1, .25);
            Assert.AreEqual(2.75, result.Error, 1e-6); Assert.IsTrue(result.IsMatch);
        }
        [Test] public void ZeroWeightAndOptionalRotations_DoNotRequirePositions()
        {
            var joints = RecognitionFixtures.Canonical().CopyJoints();
            for (int i = 1; i < 26; i++) joints[i] = JointPose.Unavailable((HandJointId)i);
            joints[0].HasRotation = false;
            var q = RecognitionFixtures.Canonical(joints: joints); var weights = RecognitionFixtures.Weights(0);
            Assert.IsTrue(ShapeRecognizer.Recognize(q, Catalog(Pose("positions", q, weights)), 1, .25).IsMatch);
        }
        [Test] public void MinimumSampleAndStableSampleTie()
        {
            var q = RecognitionFixtures.Canonical(); var weights = RecognitionFixtures.Weights(1);
            var catalog = Catalog(Pose("multi", Offset(q, 1, Vector3.one), weights, .1f, q, q));
            Assert.AreEqual(1, ShapeRecognizer.Recognize(q, catalog, 1, .25).SampleIndex);
        }
        [TestCase(.499f, true)] [TestCase(.5f, true)] [TestCase(.501f, false)]
        public void InclusiveThreshold(float distance, bool match)
        {
            var joints = HandFrame.CreateUnavailableJoints(); joints[0] = new JointPose(HandJointId.Wrist, Vector3.zero, default, true, false);
            var p = RecognitionFixtures.Canonical(joints: joints); joints[0].Position.x = distance;
            var result = ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(joints: joints), Catalog(Pose("boundary", p, RecognitionFixtures.Weights(0), .5f)), 1, .25);
            Assert.AreEqual(match, result.IsMatch); Assert.IsTrue(result.HasScore);
            if (!match) { Assert.IsNull(result.PoseId); Assert.AreEqual("boundary", result.BestCandidateId); }
        }
        [Test] public void BestAboveOwnThreshold_DoesNotChooseWiderHigherErrorPose()
        {
            var q = RecognitionFixtures.Canonical(); var w = RecognitionFixtures.Weights(0);
            var catalog = Catalog(Pose("best", Offset(q, 0, Vector3.right), w, .5f), Pose("wide", Offset(q, 0, Vector3.right * 2), w, 3));
            var result = ShapeRecognizer.Recognize(q, catalog, 1, .25);
            Assert.AreEqual(RecognitionStatus.AboveThreshold, result.Status); Assert.AreEqual("best", result.BestCandidateId); Assert.IsNull(result.PoseId);
        }
        [Test] public void OrdinalIdTieAndSideFilter()
        {
            var q = RecognitionFixtures.Canonical(); var w = RecognitionFixtures.Weights(0);
            var catalog = Catalog(Pose("a", q, w), Pose("A", q, w), Pose("0-left", RecognitionFixtures.Canonical(side: HandSide.Left), w));
            Assert.AreEqual("A", ShapeRecognizer.Recognize(q, catalog, 1, .25).PoseId);
        }
        [TestCase(1.25, RecognitionStatus.Matched)] [TestCase(1.250001, RecognitionStatus.Stale)] [TestCase(.99, RecognitionStatus.InvalidFrame)]
        public void FreshnessInclusiveAndFuture(double now, RecognitionStatus status)
        {
            var q = RecognitionFixtures.Canonical();
            Assert.AreEqual(status, ShapeRecognizer.Recognize(q, Catalog(Pose("shape", q, RecognitionFixtures.Weights(0))), now, .25).Status);
        }
        [TestCase(double.NaN, .25)] [TestCase(double.PositiveInfinity, .25)] [TestCase(-1, .25)]
        [TestCase(1, 0)] [TestCase(1, -1)] [TestCase(1, double.NaN)] [TestCase(1, double.PositiveInfinity)]
        public void InvalidSettings(double now, double age)
        { AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(), null, now, age), RecognitionStatus.InvalidSettings); }
        [Test] public void DefaultAndUnavailableAreUnscored()
        {
            AssertUnscored(default, RecognitionStatus.Unavailable);
            AssertUnscored(ShapeRecognizer.Recognize(null, null, 1, .25), RecognitionStatus.InvalidFrame);
            AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Raw(tracked: false), null, 1, .25), RecognitionStatus.Untracked);
            AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Raw(), null, 1, .25), RecognitionStatus.UnsupportedFrame);
            AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(), null, 1, .25), RecognitionStatus.CatalogUnavailable);
        }
        [Test] public void NoCandidatesAndRequiredJointLossAreUnscored()
        {
            var q = RecognitionFixtures.Canonical(); AssertUnscored(ShapeRecognizer.Recognize(q, Catalog(), 1, .25), RecognitionStatus.NoCandidates);
            var catalog = Catalog(Pose("shape", q, RecognitionFixtures.Weights(1)));
            var joints = q.CopyJoints(); joints[1] = JointPose.Unavailable(HandJointId.Palm);
            AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(joints: joints), catalog, 1, .25), RecognitionStatus.NoEligibleSamples);
            AssertUnscored(ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(side: HandSide.Left), catalog, 1, .25), RecognitionStatus.NoCandidates);
        }
        [Test] public void FiniteExtremeCoordinatesAndWeights_UseDoubleArithmetic()
        {
            var joints = HandFrame.CreateUnavailableJoints(); joints[0] = new JointPose(HandJointId.Wrist, Vector3.one * float.MaxValue, default, true, false);
            var p = RecognitionFixtures.Canonical(joints: joints); joints[0].Position = Vector3.one * -float.MaxValue;
            var w = RecognitionFixtures.Weights(0); w[0] = float.MaxValue;
            var result = ShapeRecognizer.Recognize(RecognitionFixtures.Canonical(joints: joints), Catalog(Pose("extreme", p, w)), 1, .25);
            Assert.IsTrue(result.HasScore); Assert.IsFalse(double.IsInfinity(result.Error)); Assert.Greater(result.Error, (double)float.MaxValue);
        }
        [Test] public void RetainedInputAndSnapshotStayUnchanged()
        {
            var q = RecognitionFixtures.Canonical(); var w = RecognitionFixtures.Weights(0); var asset = Pose("shape", q, w); var catalog = Catalog(asset);
            var before = q.CopyJoints(); ShapeRecognizer.Recognize(q, catalog, 1, .25);
            asset.SetDefinition("changed", "changed", HandSide.Right, null, w, 0);
            Assert.AreEqual("shape", ShapeRecognizer.Recognize(q, catalog, 1, .25).PoseId);
            CollectionAssert.AreEqual(before, q.CopyJoints());
        }
        static void AssertUnscored(RecognitionResult result, RecognitionStatus status)
        { Assert.AreEqual(status, result.Status); Assert.IsFalse(result.HasScore); Assert.IsFalse(result.IsMatch); Assert.IsNull(result.PoseId); }
        [Test] public void UnknownSideSpaceAndNoAvailablePositionsAreRejected()
        {
            var joints = HandFrameFixtures.Joints();
            var unknownSide = new HandFrame(1, 1, HandSide.Unknown, true, HandFrameSpace.CanonicalHandSpace, joints, normalizationVersion: 1);
            AssertUnscored(ShapeRecognizer.Recognize(unknownSide, null, 1, .25), RecognitionStatus.InvalidFrame);
            var unknownSpace = new HandFrame(1, 1, HandSide.Right, true, HandFrameSpace.Unknown, joints);
            AssertUnscored(ShapeRecognizer.Recognize(unknownSpace, null, 1, .25), RecognitionStatus.UnsupportedFrame);
            var empty = RecognitionFixtures.Canonical(joints: HandFrame.CreateUnavailableJoints());
            AssertUnscored(ShapeRecognizer.Recognize(empty, null, 1, .25), RecognitionStatus.InvalidFrame);
        }
        [TestCase(2, 1)] [TestCase(1, 2)]
        public void UnsupportedVersionsCannotCrossFrameBoundary(int layout, int normalization)
        {
            Assert.Throws<ArgumentException>(() => new HandFrame(1, 1, HandSide.Right, true,
                HandFrameSpace.CanonicalHandSpace, HandFrameFixtures.Joints(), layout, normalization));
        }
    }
}
