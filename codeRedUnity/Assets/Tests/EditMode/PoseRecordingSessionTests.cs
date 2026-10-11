using NUnit.Framework;

namespace HandPoseRecognition.Tests
{
    public class PoseRecordingSessionTests
    {
        PoseRecordingSession m_Session;
        PoseRecordingSettings m_Settings;
        object m_Source;
        [SetUp] public void Setup()
        {
            m_Session = new PoseRecordingSession(); m_Settings = new PoseRecordingSettings(); m_Source = new object();
            Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 1, out var error), error);
        }
        void Tick(double time, ulong sequence, bool tracked = true)
            => m_Session.Tick(m_Source, RecognitionFixtures.Raw(time, sequence, tracked), time);
        [Test] public void BeginCopiesDefinitionWeightsAndSamplesAreOwned()
        {
            m_Settings.PoseId = "changed"; m_Settings.Weights[0] = 0; m_Settings.Side = HandSide.Left;
            Tick(1, 1); m_Session.Stop(); var review = m_Session.ExportReview();
            Assert.AreEqual("recorded.shape", review.CopyDefinition().PoseId); Assert.AreEqual(1, review.CopyDefinition().Weights[0]);
            var copy = review.CopySamples()[0].CopyJoints(); copy[0].HasPosition = false;
            Assert.IsTrue(review.CopySamples()[0].TryGetJoint(HandJointId.Wrist, out _));
            m_Session.Clear(); Assert.AreEqual(1, review.CopySamples().Length);
        }
        [Test] public void DuplicateSequencesNeverCountAsNewAttemptsEvenAfterAnotherFrame()
        {
            Tick(1, 1); Tick(1, 1); Tick(1.2, 2); m_Session.Tick(m_Source, RecognitionFixtures.Raw(1, 1), 1.2);
            Assert.AreEqual(2, m_Session.AttemptCount); Assert.AreEqual(2, m_Session.AcceptedCount);
        }
        [Test] public void IntervalSkippedCountsRejectedAndInclusiveBoundaryAccepts()
        {
            Tick(1, 1); Tick(1.05, 2); Tick(1.125, 3);
            Assert.AreEqual(2, m_Session.AcceptedCount); Assert.AreEqual(1, m_Session.RejectedCount);
            Assert.That(m_Session.LastRejectionReason, Does.Contain("interval"));
        }
        [Test] public void ExactIntervalBoundary()
        {
            m_Session.Cancel(); m_Settings.MinimumSampleIntervalSeconds = .125;
            Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 1, out _)); Tick(1, 1); Tick(1.125, 2);
            Assert.AreEqual(2, m_Session.AcceptedCount);
        }
        [TestCase(1.25, 1)] [TestCase(1.250001, 0)] [TestCase(.99, 0)]
        public void FreshnessAndFuture(double now, int accepted)
        {
            m_Session.Cancel(); Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 0, out _));
            m_Session.Tick(m_Source, RecognitionFixtures.Raw(1), now); Assert.AreEqual(accepted, m_Session.AcceptedCount);
        }
        [Test] public void SideMismatchRejectsAndSourceReplacementStops()
        {
            m_Session.Tick(m_Source, RecognitionFixtures.Raw(side: HandSide.Left), 1);
            Assert.AreEqual(1, m_Session.RejectedCount); m_Session.Tick(new object(), RecognitionFixtures.Raw(), 1);
            Assert.AreEqual(RecordingCompletion.SourceReplaced, m_Session.Completion); Assert.AreEqual(RecordingState.ReadyForReview, m_Session.State);
        }
        [Test] public void TrackingAndMissingAnchorAndWeightedJointReject_RecoveryContinues()
        {
            Tick(1, 1, false);
            var joints = HandFrameFixtures.Joints(); joints[0].HasPosition = false;
            m_Session.Tick(m_Source, RecognitionFixtures.Raw(1.125, 2, joints: joints), 1.125);
            Assert.That(m_Session.LastRejectionReason, Does.Contain("Wrist"));
            joints = HandFrameFixtures.Joints(); joints[1].HasPosition = false;
            m_Session.Tick(m_Source, RecognitionFixtures.Raw(1.25, 3, joints: joints), 1.25);
            Assert.That(m_Session.LastRejectionReason, Does.Contain("Palm")); Tick(1.5, 4);
            Assert.AreEqual(3, m_Session.RejectedCount); Assert.AreEqual(1, m_Session.AcceptedCount);
        }
        [Test] public void AcceptedLimitStopsWithoutOverwrite()
        {
            m_Session.Cancel(); m_Settings.MaxAcceptedSamples = 2; Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 1, out _));
            Tick(1, 1); Tick(1.125, 2); Tick(1.25, 3);
            Assert.AreEqual(2, m_Session.AcceptedCount); Assert.AreEqual(2, m_Session.AttemptCount);
            Assert.AreEqual(RecordingCompletion.SampleLimit, m_Session.Completion);
        }
        [Test] public void RejectedAttemptLimitAndNoUpdateTimeoutAreBounded()
        {
            m_Session.Cancel(); m_Settings.MaxAttempts = 2; Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 1, out _));
            Tick(1, 1, false); Tick(1.125, 2, false); Assert.AreEqual(RecordingCompletion.AttemptLimit, m_Session.Completion);
            m_Session.Clear(); Assert.IsTrue(m_Session.Begin(m_Settings, m_Source, 1, out _));
            m_Session.Tick(m_Source, null, 10.999); Assert.AreEqual(RecordingState.Recording, m_Session.State);
            m_Session.Tick(m_Source, null, 11); Assert.AreEqual(RecordingCompletion.Timeout, m_Session.Completion);
        }
        [Test] public void StopZeroSamplesCancelAndClearAreExplicit()
        {
            m_Session.Stop(); Assert.AreEqual(RecordingCompletion.ManualStop, m_Session.Completion);
            Assert.AreEqual(0, m_Session.ExportReview().CopySamples().Length); m_Session.Cancel();
            Assert.AreEqual(RecordingState.Canceled, m_Session.State); Assert.Throws<System.InvalidOperationException>(() => m_Session.ExportReview());
            m_Session.Clear(); Assert.AreEqual(RecordingState.Idle, m_Session.State);
        }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(.9)]
        public void InvalidMonotonicClockStops(double now)
        { m_Session.Tick(m_Source, null, now); Assert.AreEqual(RecordingCompletion.InvalidClock, m_Session.Completion); }
        [TestCase("accepted")] [TestCase("budget")] [TestCase("attempts")] [TestCase("duration")] [TestCase("interval")]
        [TestCase("age")] [TestCase("weights")] [TestCase("id")] [TestCase("name")] [TestCase("side")] [TestCase("threshold")]
        public void InvalidBeginSettingsDoNotMutatePendingSession(string field)
        {
            m_Session.Stop();
            switch (field)
            {
                case "accepted": m_Settings.MaxAcceptedSamples = 33; break;
                case "budget": m_Settings.MaxSamplesPerPose = 0; break;
                case "attempts": m_Settings.MaxAttempts = 0; break;
                case "duration": m_Settings.MaxDurationSeconds = double.NaN; break;
                case "interval": m_Settings.MinimumSampleIntervalSeconds = -1; break;
                case "age": m_Settings.MaxFrameAgeSeconds = 0; break;
                case "weights": m_Settings.Weights = new float[26]; break;
                case "id": m_Settings.PoseId = " bad "; break;
                case "name": m_Settings.DisplayName = " "; break;
                case "side": m_Settings.Side = HandSide.Unknown; break;
                case "threshold": m_Settings.MaxError = float.PositiveInfinity; break;
            }
            Assert.IsFalse(m_Session.Begin(m_Settings, m_Source, 1, out var error)); Assert.IsNotEmpty(error);
            Assert.AreEqual(RecordingState.ReadyForReview, m_Session.State);
        }
    }
}
