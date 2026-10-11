using System;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class GestureMatcherEdgeTests
    {
        static GestureClip ShiftPath(GestureDefinition definition, float scale)
        {
            var frames = GestureTestFixtures.Observations(definition.MotionKind);
            var initial = frames[0].Right; initial.TryGetJoint(definition.TrajectoryJoint, out var first);
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i].Right.TryGetJoint(definition.TrajectoryJoint, out var tip);
                frames[i] = GestureTestFixtures.Observation(GestureTestFixtures.Frame(definition.MotionKind, (tip.Position - first.Position) / .06f * scale,
                    frames[i].TimestampSeconds, frames[i].AcquisitionId));
            }
            return GestureTestFixtures.Clip(definition, frames);
        }
        [Test] public void WinnerThresholdIsInclusiveAndNeverSelectsWorseWideThreshold()
        {
            var liveDefinition = GestureTestFixtures.Definition(); var live = GestureTestFixtures.Clip(liveDefinition);
            var strict = GestureTestFixtures.Definition(id: "a.strict"); strict.Settings.MaxError = 0;
            var wide = GestureTestFixtures.Definition(id: "b.wide"); wide.Settings.MaxError = 10;
            var a = GestureTestFixtures.Asset(strict, ShiftPath(strict, .9f)); var b = GestureTestFixtures.Asset(wide, ShiftPath(wide, .8f));
            try
            {
                var recognizer = new MotionGestureRecognizer(); var result = recognizer.Recognize(live, GestureTestFixtures.Catalog(b, a));
                Assert.AreEqual(GestureRecognitionStatus.AboveThreshold, result.Status); Assert.IsTrue(result.HasScore); Assert.IsNull(result.GestureId);
                Assert.AreEqual(strict.GestureId, result.BestCandidateId);
                double threshold = result.Error; strict.Settings.MaxError = threshold;
                a.SetDefinition(strict, new[] { ShiftPath(strict, .9f) });
                Assert.IsTrue(recognizer.Recognize(live, GestureTestFixtures.Catalog(a)).IsMatch);
                strict.Settings.MaxError = threshold * .999; a.SetDefinition(strict, new[] { ShiftPath(strict, .9f) });
                Assert.IsFalse(recognizer.Recognize(live, GestureTestFixtures.Catalog(a)).IsMatch);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }
        [Test] public void OrdinalIdsThenStableExampleIndexBreakExactTies()
        {
            var aDefinition = GestureTestFixtures.Definition(id: "A"); var zDefinition = GestureTestFixtures.Definition(id: "z");
            var clip = GestureTestFixtures.Clip(aDefinition);
            var a = GestureTestFixtures.Asset(aDefinition, clip, clip); var z = GestureTestFixtures.Asset(zDefinition, GestureTestFixtures.Clip(zDefinition));
            try
            {
                var result = new MotionGestureRecognizer().Recognize(clip, GestureTestFixtures.Catalog(z, a));
                Assert.AreEqual("A", result.GestureId); Assert.AreEqual(0, result.ExampleIndex);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(z); }
        }
        [Test] public void UnsupportedVersionAndNullEvidenceFailWithExplicitNoScore()
        {
            var clip = GestureTestFixtures.Clip();
            var bad = JsonUtility.FromJson<GestureClip>(JsonUtility.ToJson(clip).Replace("\"m_SchemaVersion\":1", "\"m_SchemaVersion\":99"));
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(bad, out var result)); Assert.IsFalse(result.HasScore);
            Assert.AreEqual(99, bad.Clone().SchemaVersion);
            var observations = new GestureObservation[2]; var missing = new GestureClip(clip.Definition, clip.Reference, observations);
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(missing, out result)); Assert.IsFalse(result.HasScore);
        }
        [Test] public void DtwRequiresBothEndpointsAndBoundsStretching()
        {
            var shortPath = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, Quaternion.identity), new GestureFeaturePoint(1, Vector3.right, Vector3.zero, Quaternion.identity) };
            var longPath = new GestureFeaturePoint[10];
            for (int i = 0; i < longPath.Length; i++) longPath[i] = new GestureFeaturePoint(i, Vector3.right * (i / 9f), Vector3.zero, Quaternion.identity);
            var settings = new GestureMotionSettings { DtwBand = 12, MaxWarpRun = 1 };
            Assert.IsFalse(new MotionGestureRecognizer().TryDtw(shortPath, longPath, settings, out _, out _));
            var endMismatch = (GestureFeaturePoint[])shortPath.Clone(); endMismatch[1] = new GestureFeaturePoint(1, Vector3.right * 4, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(new MotionGestureRecognizer().TryDtw(shortPath, endMismatch, settings, out var error, out _)); Assert.Greater(error, 0);
        }
        [Test] public void QAndNegativeQProduceExactlyTheSameOrientationCost()
        {
            var q = Quaternion.Euler(22, 38, 17); var opposite = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            var a = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, q), new GestureFeaturePoint(1, Vector3.right, Vector3.zero, q) };
            var b = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, opposite), new GestureFeaturePoint(1, Vector3.right, Vector3.zero, opposite) };
            Assert.IsTrue(new MotionGestureRecognizer().TryDtw(a, b, new GestureMotionSettings(), out var error, out _)); Assert.That(error, Is.LessThan(1e-8));
        }
        [Test] public void SourceEpochAndRawTimestampOrderingAreNeverBridged()
        {
            var definition = GestureTestFixtures.Definition(); var frames = GestureTestFixtures.Observations(); var good = GestureTestFixtures.Clip(definition, frames);
            frames[12] = GestureTestFixtures.Observation(frames[12].Right, epoch: 2);
            var invalid = new GestureClip(definition, good.Reference, frames);
            Assert.IsFalse(invalid.TryValidate(out _));
            frames = GestureTestFixtures.Observations(); frames[12] = frames[11]; invalid = new GestureClip(definition, good.Reference, frames);
            Assert.IsFalse(invalid.TryValidate(out _));
        }
        [Test] public void SnapshotBuildRejectsDuplicateIdsAndCatalogBudgetAtomically()
        {
            var d = GestureTestFixtures.Definition(); var a = GestureTestFixtures.Asset(d, GestureTestFixtures.Clip(d)); var catalog = ScriptableObject.CreateInstance<GestureCatalog>();
            try
            {
                catalog.SetEntries(new[] { a, a }); Assert.IsFalse(catalog.TryBuildSnapshot(out var snapshot, out _)); Assert.IsNull(snapshot);
                catalog.SetEntries(new[] { a }, maxEntries: 0); Assert.IsFalse(catalog.TryBuildSnapshot(out snapshot, out _)); Assert.IsNull(snapshot);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(catalog); }
        }
        [Test] public void JRequiresWristRotationAndDoesNotReduceToHeldI()
        {
            var d = GestureTestFixtures.Definition(GestureMotionKind.J); var frames = GestureTestFixtures.Observations(GestureMotionKind.J); var clip = GestureTestFixtures.Clip(d, frames);
            var joints = frames[20].Right.CopyJoints(); joints[0].HasRotation = false;
            frames[20] = GestureTestFixtures.Observation(new HandFrame(frames[20].TimestampSeconds, frames[20].AcquisitionId, HandSide.Right, true, HandFrameSpace.TrackingSpace, joints));
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(new GestureClip(d, clip.Reference, frames), out _));
            d.Settings.OrientationWeight = 0; Assert.IsFalse(d.TryValidate(out _));
        }
        [Test] public void OverflowingFeatureWeightSumCannotProduceAFalseZeroScore()
        {
            var definition = GestureTestFixtures.Definition(); definition.Settings.TipWeight = double.MaxValue; definition.Settings.WristWeight = double.MaxValue;
            Assert.IsFalse(definition.TryValidate(out _));
            var points = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, Quaternion.identity), new GestureFeaturePoint(1, Vector3.right, Vector3.zero, Quaternion.identity) };
            Assert.IsFalse(new MotionGestureRecognizer().TryDtw(points, points, definition.Settings, out _, out _));
        }
    }
}
