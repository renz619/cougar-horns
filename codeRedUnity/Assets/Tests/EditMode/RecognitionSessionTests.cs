using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class RecognitionSessionTests
    {
        readonly List<UnityEngine.Object> m_Owned = new List<UnityEngine.Object>();
        RecognitionSession m_Session;
        RecognitionTimingSettings m_Timing;
        PoseDatabaseSnapshot m_Catalog;
        object m_Source;
        int m_Events;
        RecognitionResult m_Event;

        public static HandFrame Frame(double time, ulong sequence, float shape = 0, bool tracked = true)
        {
            var joints = HandFrameFixtures.Joints(); joints[1].Position += Vector3.right * shape * .08f;
            return RecognitionFixtures.Raw(time, sequence, tracked, joints: joints);
        }
        PoseDatabaseSnapshot MakeCatalog()
        {
            var db = ScriptableObject.CreateInstance<PoseDatabase>(); m_Owned.Add(db);
            var poses = new HandPoseData[2]; var weights = RecognitionFixtures.Weights(1);
            for (int i = 0; i < 2; i++)
            {
                var asset = ScriptableObject.CreateInstance<HandPoseData>(); m_Owned.Add(asset); poses[i] = asset;
                Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(Frame(0, 0, i), out var q, out _));
                asset.SetDefinition(i == 0 ? "a" : "b", i == 0 ? "Shape A" : "Shape B", HandSide.Right,
                    new[] { RecognitionFixtures.Sample(q, weights) }, weights, .01f);
            }
            db.SetTemplates(poses); Assert.IsTrue(db.TryBuildSnapshot(out var result, out _)); return result;
        }
        [SetUp] public void Setup()
        {
            m_Session = new RecognitionSession(); m_Source = new object(); m_Catalog = MakeCatalog();
            m_Timing = new RecognitionTimingSettings { MinimumEvaluationIntervalSeconds = .125, ConfirmationDwellSeconds = .25, EventCooldownSeconds = .5, MaxFrameAgeSeconds = .25 };
            m_Events = 0; m_Session.PoseConfirmed += r => { m_Events++; m_Event = r; };
        }
        [TearDown] public void Cleanup() { foreach (var item in m_Owned) UnityEngine.Object.DestroyImmediate(item); m_Owned.Clear(); }
        void Tick(double now, ulong sequence, float shape = 0, bool tracked = true)
            => m_Session.Update(m_Source, Frame(now, sequence, shape, tracked), m_Catalog, RecognitionBackend.Template, m_Timing, now);
        void Repeat(HandFrame frame, double now)
            => m_Session.Update(m_Source, frame, m_Catalog, RecognitionBackend.Template, m_Timing, now);
        void Confirm() { Tick(1, 1); Tick(1.125, 2); Tick(1.25, 3); Assert.AreEqual("a", m_Session.ConfirmedPoseId); }

        [Test] public void DwellNeedsDistinctFramesAndSeparatesObservationFromConfirmation()
        {
            Tick(1, 1); Assert.IsTrue(m_Session.LatestObservation.IsMatch);
            Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(RecognitionControllerStatus.Holding, m_Session.Status);
            Repeat(Frame(1, 1), 1.25); Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(0, m_Events);
            Tick(1.25, 2); Assert.AreEqual("a", m_Session.ConfirmedPoseId); Assert.AreEqual(1, m_Events);
            Assert.AreEqual(2, m_Event.SequenceId); Assert.AreEqual(1.25, m_Event.TimestampSeconds);
        }
        [Test] public void IntervalEvaluatesLatestSkippedAcquisitionOnceAtBoundary()
        {
            Tick(1, 1); Tick(1.05, 2, 1); Tick(1.1, 3, 1); Assert.AreEqual(1, m_Session.LatestObservation.SequenceId);
            Repeat(Frame(1.1, 3, 1), 1.125); Assert.AreEqual(3, m_Session.LatestObservation.SequenceId);
            Assert.AreEqual("b", m_Session.PendingPoseId); Repeat(Frame(1.1, 3, 1), 1.25);
            Assert.IsNull(m_Session.ConfirmedPoseId);
        }
        [Test] public void PoseChangeResetsDwellAndImmediatelyReleasesConfirmedPose()
        {
            Confirm(); Tick(1.375, 4, 1); Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual("b", m_Session.PendingPoseId);
            Tick(1.5, 5, 1); Assert.IsNull(m_Session.ConfirmedPoseId); Tick(1.625, 6, 1);
            Assert.AreEqual("b", m_Session.ConfirmedPoseId); Assert.AreEqual(1, m_Events); // Newly confirmed but still under cooldown.
        }
        [Test] public void AboveThresholdReleasesImmediatelyAndHasDiagnosticScore()
        {
            Confirm(); Tick(1.375, 4, .5f); Assert.IsNull(m_Session.ConfirmedPoseId); Assert.IsNull(m_Session.PendingPoseId);
            Assert.AreEqual(RecognitionStatus.AboveThreshold, m_Session.LatestObservation.Status); Assert.IsTrue(m_Session.LatestObservation.HasScore);
            Assert.IsNull(m_Session.LatestObservation.PoseId); Assert.AreEqual(RecognitionControllerStatus.NoMatch, m_Session.Status);
        }
        [Test] public void TrackingLossWhileRateLimitedClearsImmediatelyAndRecoveryStartsDwell()
        {
            Confirm(); Tick(1.26, 4, tracked: false); Assert.IsNull(m_Session.ConfirmedPoseId);
            Assert.AreEqual(RecognitionStatus.Untracked, m_Session.LatestObservation.Status); Assert.IsFalse(m_Session.LatestObservation.HasScore);
            Tick(1.375, 5); Assert.IsNull(m_Session.ConfirmedPoseId); Tick(1.625, 6); Assert.AreEqual("a", m_Session.ConfirmedPoseId);
        }
        [Test] public void StaleRepeatWhileRateLimitedClearsAtExclusiveAgeBoundary()
        {
            Confirm(); m_Timing.MinimumEvaluationIntervalSeconds = 10;
            Repeat(Frame(1.25, 3), 1.5); Assert.AreEqual("a", m_Session.ConfirmedPoseId);
            Repeat(Frame(1.25, 3), 1.500001); Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(RecognitionStatus.Stale, m_Session.LatestObservation.Status);
        }
        [Test] public void NormalizationFailureOnSkippedNewFrameSurfacesActualReasonAndClearsHold()
        {
            Confirm(); var joints = HandFrameFixtures.Joints(); joints[0].HasPosition = false;
            Repeat(RecognitionFixtures.Raw(1.26, 4, joints: joints), 1.26);
            Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(RecognitionStatus.NormalizationFailed, m_Session.LatestObservation.Status);
            Assert.That(m_Session.LatestObservation.Reason, Does.Contain("Wrist")); Assert.IsFalse(m_Session.LatestObservation.HasScore);
        }
        [Test] public void CooldownDoesNotRepeatContinuingHoldAndDelayedReholdEmitsOnce()
        {
            Confirm(); Tick(1.375, 4); Tick(1.75, 5); Assert.AreEqual(1, m_Events);
            Tick(1.875, 6, .5f); Tick(2, 7); Tick(2.25, 8); Assert.AreEqual(2, m_Events);
            Tick(2.375, 9, .5f); Tick(2.5, 10); Tick(2.75, 11); Assert.AreEqual(3, m_Events);
            Tick(3.25, 12); Assert.AreEqual(3, m_Events);
        }
        [Test] public void SuppressedNewHoldIsTrueThenEmitsAtCooldownBoundaryWithFreshSupport()
        {
            m_Timing.ConfirmationDwellSeconds = 0; Tick(1, 1); Tick(1.125, 2, .5f); Tick(1.25, 3);
            Assert.AreEqual("a", m_Session.ConfirmedPoseId); Assert.AreEqual(1, m_Events);
            Repeat(Frame(1.25, 3), 1.5); Assert.AreEqual(2, m_Events); Repeat(Frame(1.25, 3), 1.5); Assert.AreEqual(2, m_Events);
        }
        [Test] public void SuppressedHoldCannotEmitAfterFreshnessExpires()
        {
            m_Timing.ConfirmationDwellSeconds = 0; m_Timing.EventCooldownSeconds = 2;
            Tick(1, 1); Tick(1.125, 2, .5f); Tick(1.25, 3); Repeat(Frame(1.25, 3), 3);
            Assert.AreEqual(1, m_Events); Assert.IsNull(m_Session.ConfirmedPoseId);
        }
        [Test] public void SourceAndSnapshotReplacementClearCacheAndDwellEvenWithSameSequence()
        {
            Confirm(); m_Source = new object(); Tick(1.25, 3); Assert.IsNull(m_Session.ConfirmedPoseId);
            Tick(1.5, 4); Assert.AreEqual("a", m_Session.ConfirmedPoseId);
            m_Catalog = MakeCatalog(); Tick(1.5, 4); Assert.IsNull(m_Session.ConfirmedPoseId);
        }
        [Test] public void BackendSwitchAndDisableReenableResetCooldownDeterministically()
        {
            Confirm(); m_Session.Update(m_Source, Frame(1.25, 3), m_Catalog, RecognitionBackend.LegacyRules, m_Timing, 1.25);
            Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(RecognitionControllerStatus.LegacyRules, m_Session.Status);
            m_Timing.ConfirmationDwellSeconds = 0; Tick(1.25, 3); Assert.AreEqual(2, m_Events);
            m_Session.Reset(); Assert.IsNull(m_Session.ConfirmedPoseId); Assert.IsFalse(m_Session.LatestObservation.HasScore);
            Tick(1.25, 3); Assert.AreEqual(3, m_Events);
        }
        [Test] public void NullCatalogAndSourceCannotMatch()
        {
            Confirm(); m_Catalog = null; Tick(1.375, 4); Assert.AreEqual(RecognitionStatus.CatalogUnavailable, m_Session.LatestObservation.Status);
            m_Source = null; Tick(1.5, 5); Assert.AreEqual(RecognitionStatus.Unavailable, m_Session.LatestObservation.Status); Assert.IsNull(m_Session.ConfirmedPoseId);
        }
        [TestCase("age")] [TestCase("interval")] [TestCase("dwell")] [TestCase("cooldown")] [TestCase("clock")]
        public void InvalidSettingsOrClockClearConfirmedState(string field)
        {
            Confirm(); double now = 1.375;
            switch (field)
            {
                case "age": m_Timing.MaxFrameAgeSeconds = 0; break;
                case "interval": m_Timing.MinimumEvaluationIntervalSeconds = double.PositiveInfinity; break;
                case "dwell": m_Timing.ConfirmationDwellSeconds = -1; break;
                case "cooldown": m_Timing.EventCooldownSeconds = double.NaN; break;
                case "clock": now = 1; break;
            }
            Repeat(Frame(1.25, 3), now); Assert.AreEqual(RecognitionControllerStatus.InvalidSettings, m_Session.Status);
            Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(1, m_Events);
        }
        [Test] public void FutureAcquisitionAndUnavailableFrameClearWithoutScoring()
        {
            Confirm(); Repeat(Frame(2, 4), 1.26); Assert.AreEqual(RecognitionStatus.InvalidFrame, m_Session.LatestObservation.Status);
            Repeat(null, 1.375); Assert.AreEqual(RecognitionStatus.InvalidFrame, m_Session.LatestObservation.Status); Assert.IsNull(m_Session.ConfirmedPoseId);
        }
        [Test] public void DelayedEvaluationDoesNotManufactureAcquisitionDwell()
        {
            Tick(1, 1); Repeat(Frame(1.05, 2), 1.25);
            Assert.IsNull(m_Session.ConfirmedPoseId); Assert.AreEqual(RecognitionControllerStatus.Holding, m_Session.Status);
            Tick(1.375, 3); Assert.AreEqual("a", m_Session.ConfirmedPoseId);
        }
    }
}
