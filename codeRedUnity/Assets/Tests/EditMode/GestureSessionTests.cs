using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class GestureSessionTests
    {
        GestureData m_Data;
        GestureCatalogSnapshot m_Catalog;
        GestureCalibration m_Calibration;
        [SetUp] public void SetUp()
        { var d = GestureTestFixtures.Definition(); m_Data = GestureTestFixtures.Asset(d, GestureTestFixtures.Clip(d)); m_Catalog = GestureTestFixtures.Catalog(m_Data); m_Calibration = new GestureCalibration(Quaternion.identity, 1, 1); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(m_Data); }
        [Test] public void CompletePerformanceEmitsOnceAndNeedsDistinctReleaseAndNewPerformance()
        {
            var session = new GestureRecognitionSession(); int events = 0; session.GestureConfirmed += _ => events++;
            var frames = GestureTestFixtures.Observations();
            foreach (var frame in frames) session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            Assert.AreEqual(1, events, session.Reason); Assert.AreEqual("synthetic.motion.Z", session.ConfirmedGestureId);
            ulong seq = 100;
            for (int i = 0; i < 40; i++)
            {
                var frame = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, new Vector3(.8f, -.8f, 0), 11.02 + i * .02, seq++));
                session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            }
            Assert.AreEqual(1, events, "Holding through cooldown must not replay completion.");
            for (int i = 0; i < 15; i++)
            {
                var frame = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, 11.82 + i * .02, seq++, wrongShape: true));
                session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            }
            Assert.AreEqual(GestureRecognitionState.Idle, session.State, session.Reason);
            foreach (var frame in GestureTestFixtures.Observations(start: 12.12, firstSequence: seq)) session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            Assert.AreEqual(2, events, session.Reason);
        }
        [Test] public void RepeatedFrameDoesNotCompleteAndLossClearsSuccessImmediately()
        {
            var session = new GestureRecognitionSession(); int events = 0; session.GestureConfirmed += _ => events++;
            var frames = GestureTestFixtures.Observations();
            session.Update(frames[0], m_Calibration, m_Catalog, 10); session.Update(frames[0], m_Calibration, m_Catalog, 10.1);
            Assert.AreEqual(0, events); Assert.AreEqual(1, session.FrameCount);
            foreach (var frame in frames) session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            // The clock intentionally moved backwards above; explicitly reset before the independent success check.
            session.Reset(); foreach (var frame in frames) session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            Assert.IsNotNull(session.ConfirmedGestureId, session.Reason);
            session.Update(null, m_Calibration, m_Catalog, 11.01); Assert.IsNull(session.ConfirmedGestureId); Assert.IsFalse(session.LatestObservation.HasScore);
        }
        [TestCase("epoch")] [TestCase("source")] [TestCase("catalog")] [TestCase("clock")] [TestCase("gap")]
        public void ContextChangesAndGapsCannotContinuePartialMotion(string change)
        {
            var session = new GestureRecognitionSession(); var frames = GestureTestFixtures.Observations(); int events = 0; session.GestureConfirmed += _ => events++;
            for (int i = 0; i < 12; i++) session.Update(frames[i], m_Calibration, m_Catalog, frames[i].TimestampSeconds);
            if (change == "epoch") session.Update(frames[12], new GestureCalibration(Quaternion.identity, 1, 2), m_Catalog, frames[12].TimestampSeconds);
            if (change == "source") session.Update(GestureTestFixtures.Observation(frames[12].Right, "other"), m_Calibration, m_Catalog, frames[12].TimestampSeconds);
            if (change == "catalog") session.Update(frames[12], m_Calibration, null, frames[12].TimestampSeconds);
            if (change == "clock") session.Update(frames[12], m_Calibration, m_Catalog, double.NaN);
            if (change == "gap") session.Update(frames[11], m_Calibration, m_Catalog, frames[11].TimestampSeconds + .13);
            Assert.IsNull(session.ConfirmedGestureId); Assert.AreEqual(0, events); Assert.IsFalse(session.LatestObservation.IsMatch);
        }
        [Test] public void HoldingStartShapeTimesOutWithoutStaticOrMotionEvent()
        {
            var session = new GestureRecognitionSession(); int events = 0; session.GestureConfirmed += _ => events++;
            for (int i = 0; i < 120; i++)
            {
                var frame = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, 10 + i * .02, (ulong)i));
                session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            }
            Assert.AreEqual(0, events); Assert.IsNull(session.ConfirmedGestureId); Assert.AreEqual(GestureRecognitionState.AwaitRearm, session.State);
            Assert.LessOrEqual(session.FrameCount, 12);
        }
        [TestCase(0)] [TestCase(-.01)]
        public void NewSequenceWithNonAdvancingReleaseTimeClearsSuccess(double offset)
        {
            var session = new GestureRecognitionSession();
            var frames = GestureTestFixtures.Observations();
            foreach (var frame in frames) session.Update(frame, m_Calibration, m_Catalog, frame.TimestampSeconds);
            Assert.IsNotNull(session.ConfirmedGestureId, session.Reason);
            var last = frames[frames.Length - 1];
            var release = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z,
                Vector3.zero, last.TimestampSeconds + offset, last.AcquisitionId + 1, wrongShape: true));
            session.Update(release, m_Calibration, m_Catalog, last.TimestampSeconds + .02);
            Assert.AreEqual(GestureRecognitionState.AwaitRearm, session.State);
            Assert.IsNull(session.ConfirmedGestureId);
            Assert.IsFalse(session.LatestObservation.HasScore);
            StringAssert.Contains("timestamps", session.Reason);
        }
        [Test] public void JCompletesWithWristRotationAndHeadHeadingChangesAfterArming()
        {
            var definition = GestureTestFixtures.Definition(GestureMotionKind.J); var clip = GestureTestFixtures.Clip(definition);
            var asset = GestureTestFixtures.Asset(definition, clip);
            try
            {
                var catalog = GestureTestFixtures.Catalog(asset); var session = new GestureRecognitionSession(); int events = 0;
                session.GestureConfirmed += result => { events++; Assert.AreEqual(definition.GestureId, result.GestureId); };
                for (int i = 0; i < clip.Observations.Count; i++)
                {
                    var frame = clip.Observations[i];
                    var calibration = i == 0 ? m_Calibration : new GestureCalibration(Quaternion.Euler(0, 80, 0), 1, 1);
                    session.Update(frame, calibration, catalog, frame.TimestampSeconds);
                }
                Assert.AreEqual(1, events, session.Reason); Assert.AreEqual(definition.GestureId, session.ConfirmedGestureId);
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
