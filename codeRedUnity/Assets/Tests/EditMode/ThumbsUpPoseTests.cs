using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Tests for ClassifyPoseWithOrientation: ThumbsUp, its precedence over
    /// Fist, and unchanged v0.2a results otherwise.
    /// </summary>
    public class ThumbsUpPoseTests
    {
        const float k_Straight = 0f;
        const float k_OpenBend = 5f;
        const float k_Curled = 80f;
        const float k_Splay = 10f;

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        // Thumbs from FakeHandData.Thumb: out ~0.906 (Extended), tucked ~0.330
        // (Curled), between ~0.561 (Unknown).
        static ThumbJoints ThumbOut() => FakeHandData.Thumb(45f, 0f);
        static ThumbJoints ThumbTucked() => FakeHandData.Thumb(0f, 60f);
        static ThumbJoints ThumbBetween() => FakeHandData.Thumb(15f, 0f);

        // Attach a tracking-space rotation that puts this hand's thumb at the
        // given angle from world up. A missing thumb has no direction, so it gets
        // an arbitrary valid rotation instead.
        static HandJointData Oriented(HandJointData hand, float angleFromUp)
        {
            var rotation = hand.thumb.isValid
                ? FakeHandData.ThumbUpRotation(hand, angleFromUp)
                : Quaternion.Euler(20f, 30f, 40f);
            return FakeHandData.WithTrackingSpace(hand, rotation);
        }

        static HandJointData FistWith(ThumbJoints thumb) =>
            FakeHandData.WithThumb(FakeHandData.Hand(k_Curled), thumb);

        static HandJointData ThumbsUpHand(float angleFromUp = 0f) => Oriented(FistWith(ThumbOut()), angleFromUp);

        HandPose Pose(in HandJointData hand) => HandPoseClassifier.ClassifyPoseWithOrientation(hand, m_Settings);

        // --- Clean ThumbsUp --------------------------------------------------------

        [Test]
        public void ThumbsUp_Clean()
        {
            Assert.AreEqual(HandPose.ThumbsUp, Pose(ThumbsUpHand()));
        }

        // --- Fist-shaped hands: the one exception vs. ordinary Fist cases ------------

        public enum FistCase
        {
            ThumbOutUp,           // the intentional exception -> ThumbsUp
            ThumbOutSideways,
            ThumbOutDown,
            ThumbOutNoOrientation,
            ThumbTuckedUp,
            ThumbTuckedSideways,
            ThumbBetweenUp,
            ThumbMissing,
        }

        static HandJointData BuildFist(FistCase fistCase)
        {
            switch (fistCase)
            {
                case FistCase.ThumbOutUp: return ThumbsUpHand(0f);
                case FistCase.ThumbOutSideways: return ThumbsUpHand(90f);
                case FistCase.ThumbOutDown: return ThumbsUpHand(180f);
                case FistCase.ThumbOutNoOrientation: return FistWith(ThumbOut());
                case FistCase.ThumbTuckedUp: return Oriented(FistWith(ThumbTucked()), 0f);
                case FistCase.ThumbTuckedSideways: return Oriented(FistWith(ThumbTucked()), 90f);
                case FistCase.ThumbBetweenUp: return Oriented(FistWith(ThumbBetween()), 0f);
                default: return Oriented(FistWith(default), 0f);
            }
        }

        // Only an Extended thumb pointing up turns a fist into ThumbsUp; every
        // other fist-shaped hand stays Fist, exactly as Classify reports.
        [TestCase(FistCase.ThumbOutUp, HandPose.ThumbsUp)]
        [TestCase(FistCase.ThumbOutSideways, HandPose.Fist)]
        [TestCase(FistCase.ThumbOutDown, HandPose.Fist)]
        [TestCase(FistCase.ThumbOutNoOrientation, HandPose.Fist)]
        [TestCase(FistCase.ThumbTuckedUp, HandPose.Fist)]
        [TestCase(FistCase.ThumbTuckedSideways, HandPose.Fist)]
        [TestCase(FistCase.ThumbBetweenUp, HandPose.Fist)]
        [TestCase(FistCase.ThumbMissing, HandPose.Fist)]
        public void FistShapedHands_OnlyUpwardExtendedThumbIsThumbsUp(FistCase fistCase, HandPose expected)
        {
            var hand = BuildFist(fistCase);

            // The legacy classifiers see every one of these as Fist.
            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(hand, m_Settings));
            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.ClassifyPose(hand, m_Settings));
            Assert.AreEqual(expected, Pose(hand));
        }

        // --- Correct direction, wrong finger states ------------------------------------

        [Test]
        public void ThumbUp_IndexExtended_IsLNotThumbsUp()
        {
            var hand = Oriented(FakeHandData.WithThumb(
                FakeHandData.SplayedHand(k_Straight, k_Curled, k_Curled, k_Curled, 0f, 0f, 0f, 0f), ThumbOut()), 0f);

            Assert.AreEqual(HandPose.L, Pose(hand));
        }

        [Test]
        public void ThumbUp_LittleExtended_IsYNotThumbsUp()
        {
            var hand = Oriented(FakeHandData.WithThumb(
                FakeHandData.SplayedHand(k_Curled, k_Curled, k_Curled, k_Straight, 0f, 0f, 0f, 0f), ThumbOut()), 0f);

            Assert.AreEqual(HandPose.Y, Pose(hand));
        }

        [Test]
        public void ThumbUp_MiddleInCurlBand_IsUnknown()
        {
            // 35 per joint = 105 total: the middle finger is Unknown.
            var hand = Oriented(FakeHandData.WithThumb(
                FakeHandData.SplayedHand(k_Curled, 35f, k_Curled, k_Curled, 0f, 0f, 0f, 0f), ThumbOut()), 0f);

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        [Test]
        public void ThumbUp_RingInvalid_IsUnknown()
        {
            var hand = ThumbsUpHand();
            hand.ring.isValid = false;

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        [Test]
        public void ThumbUp_Untracked_IsUnknown()
        {
            var hand = ThumbsUpHand();
            hand.isTracked = false;

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        // --- Invalid orientation setting ------------------------------------------------

        [TestCase(float.NaN)]
        [TestCase(90f)]
        [TestCase(-5f)]
        public void InvalidOrientationSetting_ThumbsUpHandFallsBackToFist(float maxAngle)
        {
            m_Settings.thumbUpMaxAngleDegrees = maxAngle;

            Assert.AreEqual(HandPose.Fist, Pose(ThumbsUpHand()));
        }

        // --- Regression: v0.2a poses unchanged ---------------------------------------------

        public enum V02aHand
        {
            Pointing,
            L,
            Y,
            V,
            U,
            Open,
            Fist,
        }

        static HandJointData BuildV02a(V02aHand hand)
        {
            HandJointData Fingers(float i, float m, float r, float l, float iSplay = 0f, float mSplay = 0f) =>
                FakeHandData.SplayedHand(i, m, r, l, iSplay, mSplay, 0f, 0f);

            switch (hand)
            {
                case V02aHand.Pointing: return FakeHandData.WithThumb(Fingers(k_Straight, k_Curled, k_Curled, k_Curled), ThumbTucked());
                case V02aHand.L: return FakeHandData.WithThumb(Fingers(k_Straight, k_Curled, k_Curled, k_Curled), ThumbOut());
                case V02aHand.Y: return FakeHandData.WithThumb(Fingers(k_Curled, k_Curled, k_Curled, k_Straight), ThumbOut());
                case V02aHand.V: return FakeHandData.WithThumb(Fingers(k_Straight, k_Straight, k_Curled, k_Curled, -k_Splay, k_Splay), ThumbTucked());
                case V02aHand.U: return FakeHandData.WithThumb(Fingers(k_Straight, k_Straight, k_Curled, k_Curled), ThumbTucked());
                case V02aHand.Open: return FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), ThumbOut());
                default: return FistWith(ThumbTucked());
            }
        }

        [TestCase(V02aHand.Pointing, HandPose.Pointing)]
        [TestCase(V02aHand.L, HandPose.L)]
        [TestCase(V02aHand.Y, HandPose.Y)]
        [TestCase(V02aHand.V, HandPose.V)]
        [TestCase(V02aHand.U, HandPose.U)]
        [TestCase(V02aHand.Open, HandPose.Open)]
        [TestCase(V02aHand.Fist, HandPose.Fist)]
        public void V02aPoses_UnchangedWithAndWithoutOrientation(V02aHand fake, HandPose expected)
        {
            var plain = BuildV02a(fake);
            var thumbUp = Oriented(plain, 0f);

            Assert.AreEqual(expected, HandPoseClassifier.ClassifyPose(plain, m_Settings));
            Assert.AreEqual(expected, Pose(plain));
            Assert.AreEqual(expected, Pose(thumbUp));
        }

        [TestCase(V02aHand.Pointing, HandPose.Pointing)]
        [TestCase(V02aHand.V, HandPose.V)]
        [TestCase(V02aHand.Open, HandPose.Open)]
        public void InvalidOrientationSetting_DoesNotChangeV02aPoses(V02aHand fake, HandPose expected)
        {
            m_Settings.thumbUpMaxAngleDegrees = float.PositiveInfinity;

            Assert.AreEqual(expected, Pose(Oriented(BuildV02a(fake), 0f)));
        }

        [Test]
        public void NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => HandPoseClassifier.ClassifyPoseWithOrientation(ThumbsUpHand(), null));
        }
    }
}
