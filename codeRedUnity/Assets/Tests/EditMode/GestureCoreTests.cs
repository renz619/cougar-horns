using System;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class GestureCoreTests
    {
        [TestCase(GestureMotionKind.Z)] [TestCase(GestureMotionKind.J)]
        public void CompleteSyntheticPerformanceMatchesOwnedSnapshot(GestureMotionKind kind)
        {
            var definition = GestureTestFixtures.Definition(kind); var clip = GestureTestFixtures.Clip(definition);
            Assert.IsTrue(MotionGestureRecognizer.TryCheckComplete(clip, out var complete), complete.Reason);
            var asset = GestureTestFixtures.Asset(definition, clip);
            try
            {
                var snapshot = GestureTestFixtures.Catalog(asset); asset.SetDefinition(GestureTestFixtures.Definition(kind, "changed"), Array.Empty<GestureClip>());
                var result = new MotionGestureRecognizer().Recognize(clip, snapshot);
                Assert.IsTrue(result.IsMatch, result.Reason); Assert.AreEqual(definition.GestureId, result.GestureId); Assert.That(result.Error, Is.LessThan(1e-6));
                Assert.AreEqual(1, result.PhaseCoverage); Assert.AreEqual(GestureHandMask.Right, result.ParticipatingHands);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
        [TestCase(GestureMotionKind.Z, .7)] [TestCase(GestureMotionKind.Z, 1.5)]
        [TestCase(GestureMotionKind.J, .7)] [TestCase(GestureMotionKind.J, 1.5)]
        public void SpeedTranslationAndScaleRemainSupported(GestureMotionKind kind, double duration)
        {
            var definition = GestureTestFixtures.Definition(kind); var asset = GestureTestFixtures.Asset(definition, GestureTestFixtures.Clip(definition));
            try
            {
                var basis = Quaternion.Euler(0, 35, 0);
                var live = GestureTestFixtures.Clip(definition, GestureTestFixtures.Observations(kind, duration: duration, basis: basis, origin: new Vector3(1, 2, 3), scale: 1.3f), basis);
                var result = new MotionGestureRecognizer().Recognize(live, GestureTestFixtures.Catalog(asset)); Assert.IsTrue(result.IsMatch, result.Reason + " " + result.Error);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
        [TestCase(GestureMotionKind.J)] [TestCase(GestureMotionKind.Z)]
        public void ReversedAndWrongShapesFailBeforeScoring(GestureMotionKind kind)
        {
            var definition = GestureTestFixtures.Definition(kind);
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(GestureTestFixtures.Clip(definition, GestureTestFixtures.Observations(kind, reverse: true)), out _));
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(GestureTestFixtures.Clip(definition, GestureTestFixtures.Observations(kind, wrongShape: true)), out _));
        }
        [Test] public void HeldShapeAndTopStrokeDoNotCompleteZ()
        {
            var definition = GestureTestFixtures.Definition(); var observations = new GestureObservation[20];
            for (int i = 0; i < observations.Length; i++) observations[i] = GestureTestFixtures.Observation(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, 10 + i * .03, (ulong)i));
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(GestureTestFixtures.Clip(definition, observations), out _));
            var full = GestureTestFixtures.Observations(); Array.Resize(ref full, 11);
            Assert.IsFalse(MotionGestureRecognizer.TryCheckComplete(GestureTestFixtures.Clip(definition, full), out var result)); Assert.Less(result.PhaseCoverage, 1);
        }
        [Test] public void ClipOwnsDefinitionArrayAndRetainsRawRotations()
        {
            var definition = GestureTestFixtures.Definition(GestureMotionKind.J); var frames = GestureTestFixtures.Observations(GestureMotionKind.J);
            var clip = GestureTestFixtures.Clip(definition, frames); frames[0] = null; definition.Settings.MaxError = 100;
            clip.Definition.Settings.MaxFrames = 2;
            Assert.IsNotNull(clip.Observations[0]); Assert.AreEqual(.25, clip.Definition.Settings.MaxError); Assert.AreEqual(256, clip.Definition.Settings.MaxFrames);
            Assert.IsTrue(clip.Observations[10].Right.TryGetJoint(HandJointId.Wrist, out var wrist)); Assert.IsTrue(wrist.HasRotation);
            Assert.IsTrue(clip.TryValidate(out var report), report.ToString());
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidMaximumDurationIsRejected(double duration)
        { var d = GestureTestFixtures.Definition(); d.Settings.MaxDurationSeconds = duration; Assert.IsFalse(d.TryValidate(out _)); }
        [Test] public void CalibrationRejectsScaleAndVerticalHeadForward()
        {
            Assert.IsFalse(new GestureCalibration(Vector3.up, Vector3.up, Vector3.one, 1).TryGetBasis(out _, out _));
            Assert.IsFalse(new GestureCalibration(Vector3.up, Vector3.forward, new Vector3(1, 2, 1), 1).TryGetBasis(out _, out _));
            Assert.IsFalse(new GestureCalibration(Vector3.up, Vector3.forward, -Vector3.one, 1).TryGetBasis(out _, out _));
        }
        [Test] public void FutureStaticTwoHandDefinitionIsStoredButNeverRecognized()
        {
            var d = GestureTestFixtures.Definition(); d.Category = GestureCategory.Static; d.RequiredRoles = GestureHandRoles.Dominant | GestureHandRoles.NonDominant;
            d.DominantSide = HandSide.Left; d.Settings = null;
            Assert.IsTrue(d.TryValidate(out _)); Assert.AreEqual(HandSide.Right, d.NonDominantSide); Assert.IsFalse(d.IsSupported);
            var asset = GestureTestFixtures.Asset(d);
            try { var catalog = GestureTestFixtures.Catalog(asset); Assert.IsFalse(catalog.Entries[0].IsSupported); }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
        [Test] public void DtwHandComputedDiagonalCostAndQuaternionSign()
        {
            var a = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, Quaternion.identity), new GestureFeaturePoint(1, Vector3.right, Vector3.zero, Quaternion.identity) };
            var b = new[] { new GestureFeaturePoint(0, Vector3.zero, Vector3.zero, new Quaternion(0, 0, 0, -1)), new GestureFeaturePoint(1, Vector3.right * 3, Vector3.zero, Quaternion.identity) };
            var settings = new GestureMotionSettings { DtwBand = 0, WristWeight = 0, OrientationWeight = 0 };
            Assert.IsTrue(new MotionGestureRecognizer().TryDtw(a, b, settings, out var error, out var length)); Assert.AreEqual(2, length); Assert.AreEqual(1, error, 1e-12);
            settings.OrientationWeight = 1;
            Assert.IsTrue(new MotionGestureRecognizer().TryDtw(a, a, settings, out error, out _)); Assert.AreEqual(0, error, 1e-12);
        }
    }
}
