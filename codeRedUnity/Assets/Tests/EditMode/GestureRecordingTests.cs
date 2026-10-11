using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class GestureRecordingTests
    {
        public static GestureRecordingReview Review(GestureDefinition definition = null)
        {
            definition = definition ?? GestureTestFixtures.Definition();
            var observations = GestureTestFixtures.Observations(definition.MotionKind);
            var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(definition, observations[0].SourceIdentity, new GestureCalibration(Quaternion.identity, 1, 1), observations[0], 10, out var error), error);
            for (int i = 1; i < observations.Length; i++) session.Tick(observations[i], observations[i].SourceIdentity, 1, observations[i].TimestampSeconds);
            session.Stop(); var review = session.ExportReview(); Assert.IsTrue(review.TryValidate(out _, out error), error); return review;
        }
        [Test] public void ManualWholeClipFreezesDefinitionDeduplicatesAndSerializes()
        {
            var d = GestureTestFixtures.Definition(); var frames = GestureTestFixtures.Observations(); var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(d, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frames[0], 10, out _));
            d.GestureId = "changed"; d.Settings.MaxFrames = 2;
            for (int i = 0; i < frames.Length; i++)
            { session.Tick(frames[i], "synthetic-source", 1, frames[i].TimestampSeconds); session.Tick(frames[i], "synthetic-source", 1, frames[i].TimestampSeconds); }
            Assert.AreEqual(frames.Length, session.AcceptedCount); Assert.AreEqual(frames.Length, session.AttemptCount);
            session.Stop(); var review = session.ExportReview(); Assert.AreEqual("synthetic.motion.Z", review.CopyClip().Definition.GestureId);
            var restored = JsonUtility.FromJson<GestureRecordingReview>(JsonUtility.ToJson(review)); Assert.IsTrue(restored.TryValidate(out _, out var reason), reason);
            Assert.AreEqual(frames.Length, restored.CopyClip().Observations.Count); session.Cancel(); Assert.AreEqual(0, session.AcceptedCount);
            Assert.IsTrue(restored.TryValidate(out _, out _));
        }
        [TestCase("source")] [TestCase("epoch")] [TestCase("future")] [TestCase("stale")] [TestCase("loss")]
        [TestCase("backtime")] [TestCase("backsequence")] [TestCase("joint")] [TestCase("rotation")] [TestCase("jump")]
        public void InvalidAcquisitionStopsAndCannotBeSaved(string failure)
        {
            var definition = GestureTestFixtures.Definition(); var frames = GestureTestFixtures.Observations(); var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(definition, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frames[0], 10, out _));
            GestureObservation next = frames[1]; double now = next.TimestampSeconds; string identity = "synthetic-source"; ulong epoch = 1;
            if (failure == "source") identity = "replacement";
            if (failure == "epoch") epoch = 2;
            if (failure == "future") now = 10;
            if (failure == "stale") now += .3;
            if (failure == "backtime") next = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, 9.99, 2));
            if (failure == "backsequence") next = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, now, 0));
            if (failure == "loss") next = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, now, 2, tracked: false));
            if (failure == "jump") next = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.one * 100, now, 2));
            if (failure == "joint" || failure == "rotation")
            {
                var joints = next.Right.CopyJoints();
                if (failure == "joint") joints[(int)HandJointId.IndexTip].HasPosition = false;
                else joints[(int)HandJointId.Wrist].HasRotation = false;
                next = GestureTestFixtures.Observation(new HandFrame(now, 2, HandSide.Right, true, HandFrameSpace.TrackingSpace, joints));
            }
            session.Tick(next, identity, epoch, now);
            Assert.AreEqual(GestureRecordingState.ReadyForReview, session.State); Assert.IsFalse(session.ExportReview().TryValidate(out _, out var reason)); Assert.IsNotEmpty(reason);
        }
        [TestCase(true)] [TestCase(false)] public void NoAcquisitionOrRepeatedReadCannotBridgeGap(bool repeat)
        {
            var frame = GestureTestFixtures.Observations()[0]; var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(GestureTestFixtures.Definition(), "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frame, 10, out _));
            session.Tick(repeat ? frame : null, "synthetic-source", 1, 10.13);
            Assert.AreEqual(GestureRecordingState.ReadyForReview, session.State); Assert.AreEqual(1, session.AcceptedCount); Assert.IsFalse(session.ExportReview().TryValidate(out _, out _));
        }
        [TestCase("frames")] [TestCase("attempts")] [TestCase("duration")] [TestCase("clock")]
        public void BoundsAndClockNeverTurnTruncationIntoSuccess(string bound)
        {
            var d = GestureTestFixtures.Definition(); d.Settings.MaxFrames = 3; d.Settings.PreMovementFrames = 2;
            if (bound == "attempts") d.Settings.MaxAttempts = 2;
            var frames = GestureTestFixtures.Observations(); var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(d, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frames[0], 10, out _));
            if (bound == "duration") session.Tick(null, "synthetic-source", 1, 12);
            else if (bound == "clock") session.Tick(null, "synthetic-source", 1, double.NaN);
            else for (int i = 1; i < 3; i++) session.Tick(frames[i], "synthetic-source", 1, frames[i].TimestampSeconds);
            Assert.AreEqual(GestureRecordingState.ReadyForReview, session.State); Assert.IsFalse(session.ExportReview().TryValidate(out _, out _));
        }
        [Test] public void ExplicitTrimRevalidatesFullRemainingPerformance()
        {
            var review = Review(); Assert.IsFalse(review.TryTrim(0, 10, out _, out _));
            Assert.IsTrue(review.TryTrim(0, review.CopyClip().Observations.Count - 3, out var trimmed, out var error), error);
            Assert.IsTrue(trimmed.WasTrimmed); Assert.IsTrue(trimmed.TryValidate(out _, out _));
            Assert.Greater(review.CopyClip().Observations.Count, trimmed.CopyClip().Observations.Count);
        }
        [Test] public void StopWithOnlyInitialFrameIsReviewableButUnsavableAndCancelClears()
        {
            var frame = GestureTestFixtures.Observations()[0]; var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(GestureTestFixtures.Definition(), "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frame, 10, out _));
            session.Stop(); Assert.IsFalse(session.ExportReview().TryValidate(out _, out _));
            session.Cancel(); Assert.AreEqual(GestureRecordingState.Canceled, session.State); Assert.AreEqual(0, session.AcceptedCount);
            session.Clear(); Assert.AreEqual(GestureRecordingState.Idle, session.State);
        }
        [Test] public void LongerDefaultWindowRecordsAndReplaysFiveAndAHalfSecondsAt120Hz()
        {
            var definition = new GestureDefinition();
            var knots = GestureTestFixtures.Observations();
            knots[0].Right.TryGetJoint(HandJointId.IndexTip, out var firstTip);
            var frames = new GestureObservation[661];
            for (int i = 0; i < frames.Length; i++)
            {
                float progress = (float)i * (knots.Length - 1) / (frames.Length - 1);
                int left = Mathf.Min((int)progress, knots.Length - 2);
                knots[left].Right.TryGetJoint(HandJointId.IndexTip, out var a);
                knots[left + 1].Right.TryGetJoint(HandJointId.IndexTip, out var b);
                var path = (Vector3.Lerp(a.Position, b.Position, progress - left) - firstTip.Position) / .06f;
                frames[i] = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, path,
                    10 + 5.5 * i / (frames.Length - 1), (ulong)i + 1));
            }
            var session = new GestureRecordingSession();
            Assert.IsTrue(session.Begin(definition, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frames[0], 10, out var reason), reason);
            foreach (var frame in frames) session.Tick(frame, "synthetic-source", 1, frame.TimestampSeconds);
            Assert.AreEqual(GestureRecordingState.Recording, session.State);
            Assert.AreEqual(661, session.AcceptedCount);
            session.Stop(); var review = session.ExportReview();
            Assert.IsTrue(review.TryValidate(out _, out reason), reason);
            Assert.AreEqual(5.5, review.CopyClip().DurationSeconds, 1e-9);
            var asset = GestureTestFixtures.Asset(definition, review.CopyClip());
            try { Assert.IsTrue(new MotionGestureRecognizer().Recognize(review.CopyClip(), GestureTestFixtures.Catalog(asset)).IsMatch); }
            finally { Object.DestroyImmediate(asset); }
        }
        [Test] public void LongerDefaultWindowStillInvalidatesAtItsDeadline()
        {
            var definition = new GestureDefinition(); var session = new GestureRecordingSession();
            for (int i = 0; i <= 720; i++)
            {
                double now = 10 + i / 120.0;
                var frame = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, now, (ulong)i + 1));
                if (i == 0) Assert.IsTrue(session.Begin(definition, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frame, now, out _));
                else session.Tick(frame, "synthetic-source", 1, now);
            }
            Assert.AreEqual(GestureRecordingCompletion.Timeout, session.ExportReview().Completion);
            Assert.IsFalse(session.ExportReview().TryValidate(out _, out _));
        }
    }
}
