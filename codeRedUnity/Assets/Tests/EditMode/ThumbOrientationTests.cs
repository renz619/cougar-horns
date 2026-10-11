using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Tests for the thumb's world-space direction (proximal joint to tip)
    /// and its Up / NotUp / Unknown state. Default max angle: 35 degrees.
    /// </summary>
    public class ThumbOrientationTests
    {
        const float k_Curled = 80f;
        // acos loses precision near 0 degrees, so allow a small tolerance.
        const float k_AngleTolerance = 0.05f;

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        // Fist fingers with the thumb out (ratio ~0.906, Extended), no rotation yet.
        static HandJointData BaseHand() =>
            FakeHandData.WithThumb(FakeHandData.Hand(k_Curled), FakeHandData.Thumb(45f, 0f));

        static HandJointData HandAt(float angleFromUpDeg, float azimuthDeg = 0f)
        {
            var hand = BaseHand();
            return FakeHandData.WithTrackingSpace(hand, FakeHandData.ThumbUpRotation(hand, angleFromUpDeg, azimuthDeg));
        }

        static float Angle(in HandJointData hand) => HandPoseClassifier.ThumbAngleFromWorldUpDegrees(hand);

        ThumbDirectionState State(in HandJointData hand) => HandPoseClassifier.ClassifyThumbDirection(hand, m_Settings);

        // --- Valid directions ---------------------------------------------------

        [Test]
        public void ThumbUp_ReturnsUp()
        {
            var hand = HandAt(0f);

            Assert.AreEqual(0f, Angle(hand), k_AngleTolerance);
            Assert.AreEqual(ThumbDirectionState.Up, State(hand));
        }

        [TestCase(90f)]   // sideways
        [TestCase(180f)]  // down
        public void ThumbSidewaysOrDown_ReturnsNotUp(float angleFromUp)
        {
            var hand = HandAt(angleFromUp);

            Assert.AreEqual(angleFromUp, Angle(hand), k_AngleTolerance);
            Assert.AreEqual(ThumbDirectionState.NotUp, State(hand));
        }

        [TestCase(20f, 0f)]
        [TestCase(60f, 90f)]
        [TestCase(135f, 210f)]
        public void Angle_MatchesConstructedTilt(float angleFromUp, float azimuth)
        {
            Assert.AreEqual(angleFromUp, Angle(HandAt(angleFromUp, azimuth)), k_AngleTolerance);
        }

        // --- Boundary -------------------------------------------------------------

        [Test]
        public void JustInsideBoundary_IsUp()
        {
            Assert.AreEqual(ThumbDirectionState.Up, State(HandAt(m_Settings.thumbUpMaxAngleDegrees - 0.5f)));
        }

        [Test]
        public void ExactlyAtBoundary_IsUp()
        {
            // Set the threshold to the computed angle so the inclusive comparison
            // is tested without relying on float-exact geometry.
            var hand = HandAt(30f);
            m_Settings.thumbUpMaxAngleDegrees = Angle(hand);

            Assert.AreEqual(ThumbDirectionState.Up, State(hand));
        }

        [Test]
        public void JustOutsideBoundary_IsNotUp()
        {
            Assert.AreEqual(ThumbDirectionState.NotUp, State(HandAt(m_Settings.thumbUpMaxAngleDegrees + 0.5f)));
        }

        // --- Settings -------------------------------------------------------------

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        [TestCase(90f)]
        [TestCase(120f)]
        public void InvalidMaxAngle_ReturnsUnknown(float maxAngle)
        {
            m_Settings.thumbUpMaxAngleDegrees = maxAngle;

            Assert.AreEqual(ThumbDirectionState.Unknown, State(HandAt(0f)));
            Assert.AreEqual(ThumbDirectionState.Unknown, State(HandAt(180f)));
        }

        [Test]
        public void ZeroMaxAngle_IsAccepted()
        {
            m_Settings.thumbUpMaxAngleDegrees = 0f;

            Assert.AreNotEqual(ThumbDirectionState.Unknown, State(HandAt(0f)));
            Assert.AreEqual(ThumbDirectionState.NotUp, State(HandAt(10f)));
        }

        [Test]
        public void EightyNineMaxAngle_IsAccepted()
        {
            m_Settings.thumbUpMaxAngleDegrees = 89f;

            Assert.AreEqual(ThumbDirectionState.Up, State(HandAt(85f)));
            Assert.AreEqual(ThumbDirectionState.NotUp, State(HandAt(90f)));
        }

        // --- Invalid data -----------------------------------------------------------

        void AssertUnavailable(in HandJointData hand)
        {
            Assert.IsNaN(Angle(hand));
            Assert.AreEqual(ThumbDirectionState.Unknown, State(hand));
        }

        [Test]
        public void Untracked_Unavailable()
        {
            var hand = HandAt(0f);
            hand.isTracked = false;
            AssertUnavailable(hand);
        }

        [Test]
        public void ThumbInvalid_Unavailable()
        {
            var hand = HandAt(0f);
            hand.thumb.isValid = false;
            AssertUnavailable(hand);
        }

        [Test]
        public void NaNTip_Unavailable()
        {
            var hand = HandAt(0f);
            hand.thumb.tip = new Vector3(float.NaN, 0f, 0f);
            AssertUnavailable(hand);
        }

        [Test]
        public void InfiniteTip_Unavailable()
        {
            var hand = HandAt(0f);
            hand.thumb.tip = new Vector3(0f, float.PositiveInfinity, 0f);
            AssertUnavailable(hand);
        }

        [Test]
        public void ZeroLengthDirection_Unavailable()
        {
            var hand = HandAt(0f);
            hand.thumb.tip = hand.thumb.proximal;
            AssertUnavailable(hand);
        }

        [Test]
        public void NoTrackingSpaceRotation_Unavailable()
        {
            var hand = HandAt(0f);
            hand.hasTrackingSpaceToWorld = false;
            AssertUnavailable(hand);
        }

        [Test]
        public void ZeroQuaternion_Unavailable()
        {
            AssertUnavailable(FakeHandData.WithTrackingSpace(BaseHand(), default));
        }

        [Test]
        public void ScaledQuaternion_Unavailable()
        {
            var hand = HandAt(0f);
            var q = hand.trackingSpaceToWorld;
            hand.trackingSpaceToWorld = new Quaternion(q.x * 2f, q.y * 2f, q.z * 2f, q.w * 2f);
            AssertUnavailable(hand);
        }

        [Test]
        public void NaNQuaternion_Unavailable()
        {
            AssertUnavailable(FakeHandData.WithTrackingSpace(BaseHand(), new Quaternion(float.NaN, 0f, 0f, 1f)));
        }

        // --- Translation and scale invariance ----------------------------------------

        [TestCase(0.75f)]
        [TestCase(1.4f)]
        public void TranslatedAndScaledJoints_SameAngle(float scale)
        {
            var hand = HandAt(25f, 40f);
            var moved = FakeHandData.Transformed(hand, Quaternion.identity, new Vector3(0.6f, -1.2f, 2.5f), scale);

            Assert.AreEqual(Angle(hand), Angle(moved), 1e-3f);
            Assert.AreEqual(State(hand), State(moved));
        }

        // --- Rotation -----------------------------------------------------------------

        [Test]
        public void TiltingTrackingSpaceTowardOrAwayFromUp_ChangesAngle()
        {
            var hand = HandAt(40f);
            var tiltAxis = Vector3.right; // the same horizontal axis HandAt tilted about

            var further = FakeHandData.WithTrackingSpace(hand, Quaternion.AngleAxis(20f, tiltAxis) * hand.trackingSpaceToWorld);
            var closer = FakeHandData.WithTrackingSpace(hand, Quaternion.AngleAxis(-20f, tiltAxis) * hand.trackingSpaceToWorld);

            Assert.AreEqual(60f, Angle(further), k_AngleTolerance);
            Assert.AreEqual(20f, Angle(closer), k_AngleTolerance);
            Assert.AreEqual(ThumbDirectionState.NotUp, State(further));
            Assert.AreEqual(ThumbDirectionState.Up, State(closer));
        }

        [TestCase(73f)]
        [TestCase(-150f)]
        public void RotatingTrackingSpaceAboutWorldUp_PreservesAngle(float degrees)
        {
            var hand = HandAt(40f, 15f);
            var spun = FakeHandData.WithTrackingSpace(hand, Quaternion.AngleAxis(degrees, Vector3.up) * hand.trackingSpaceToWorld);

            Assert.AreEqual(Angle(hand), Angle(spun), 1e-3f);
        }

        [Test]
        public void RotatingJointsInTrackingSpace_ChangesWorldAngle()
        {
            // Start pointing straight up, then rotate the joints 30 degrees about a
            // tracking-space axis that maps to world horizontal.
            var hand = HandAt(0f);
            var localAxis = Quaternion.Inverse(hand.trackingSpaceToWorld) * Vector3.right;
            var rotated = FakeHandData.Transformed(hand, Quaternion.AngleAxis(30f, localAxis), Vector3.zero, 1f);

            Assert.AreEqual(30f, Angle(rotated), k_AngleTolerance);
        }

        [Test]
        public void RotatedJointsWithCompensatingTrackingSpace_SameWorldAngle()
        {
            var hand = HandAt(25f, 60f);
            var jointRotation = Quaternion.Euler(37f, -120f, 65f);
            var rotated = FakeHandData.Transformed(hand, jointRotation, Vector3.zero, 1f);
            var compensated = FakeHandData.WithTrackingSpace(rotated, hand.trackingSpaceToWorld * Quaternion.Inverse(jointRotation));

            Assert.AreEqual(Angle(hand), Angle(compensated), 1e-3f);
        }

        [Test]
        public void NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => HandPoseClassifier.ClassifyThumbDirection(HandAt(0f), null));
        }
    }
}
