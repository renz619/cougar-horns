using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Tests for ClassifyPose (v0.2a poses) and its Open/Fist fallback.
    /// </summary>
    public class PoseClassifierTests
    {
        const float k_Straight = 0f;  // 0 total curl (Extended)
        const float k_OpenBend = 5f;  // 15 total (Extended)
        const float k_Curled = 80f;   // 240 total (Curled)
        const float k_Splay = 10f;    // index-middle ratio ~0.492 (Apart)

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        // Thumbs from FakeHandData.Thumb: out ~0.906 (Extended), tucked ~0.330 (Curled).
        static ThumbJoints ThumbOut() => FakeHandData.Thumb(45f, 0f);
        static ThumbJoints ThumbTucked() => FakeHandData.Thumb(0f, 60f);

        public enum ThumbCase
        {
            Out,
            Tucked,
            Missing,
        }

        static ThumbJoints ThumbFor(ThumbCase thumbCase)
        {
            switch (thumbCase)
            {
                case ThumbCase.Out:
                    return ThumbOut();
                case ThumbCase.Tucked:
                    return ThumbTucked();
                default:
                    return default;
            }
        }

        static HandJointData Fingers(float index, float middle, float ring, float little,
            float indexSplay = 0f, float middleSplay = 0f) =>
            FakeHandData.SplayedHand(index, middle, ring, little, indexSplay, middleSplay, 0f, 0f);

        // Clean fake hands for each pose.
        static HandJointData PointingHand(ThumbCase thumb = ThumbCase.Tucked) =>
            FakeHandData.WithThumb(Fingers(k_Straight, k_Curled, k_Curled, k_Curled), ThumbFor(thumb));

        static HandJointData LHand(ThumbCase thumb = ThumbCase.Out) =>
            FakeHandData.WithThumb(Fingers(k_Straight, k_Curled, k_Curled, k_Curled), ThumbFor(thumb));

        static HandJointData YHand(ThumbCase thumb = ThumbCase.Out) =>
            FakeHandData.WithThumb(Fingers(k_Curled, k_Curled, k_Curled, k_Straight), ThumbFor(thumb));

        static HandJointData VHand(ThumbCase thumb = ThumbCase.Tucked) =>
            FakeHandData.WithThumb(Fingers(k_Straight, k_Straight, k_Curled, k_Curled, -k_Splay, k_Splay), ThumbFor(thumb));

        static HandJointData UHand(ThumbCase thumb = ThumbCase.Tucked) =>
            FakeHandData.WithThumb(Fingers(k_Straight, k_Straight, k_Curled, k_Curled), ThumbFor(thumb));

        HandPose Pose(in HandJointData hand) => HandPoseClassifier.ClassifyPose(hand, m_Settings);

        // --- Clean poses ------------------------------------------------------

        [Test]
        public void Pointing_Clean() => Assert.AreEqual(HandPose.Pointing, Pose(PointingHand()));

        [Test]
        public void L_Clean() => Assert.AreEqual(HandPose.L, Pose(LHand()));

        [Test]
        public void Y_Clean() => Assert.AreEqual(HandPose.Y, Pose(YHand()));

        [Test]
        public void V_Clean() => Assert.AreEqual(HandPose.V, Pose(VHand()));

        [Test]
        public void U_Clean() => Assert.AreEqual(HandPose.U, Pose(UHand()));

        // --- Negatives --------------------------------------------------------

        [Test]
        public void L_WithThumbCurled_IsPointingNotL()
        {
            Assert.AreEqual(HandPose.Pointing, Pose(LHand(ThumbCase.Tucked)));
        }

        [Test]
        public void Pointing_WithThumbExtended_IsLNotPointing()
        {
            Assert.AreEqual(HandPose.L, Pose(PointingHand(ThumbCase.Out)));
        }

        [Test]
        public void V_WithRingExtended_IsUnknown()
        {
            var hand = FakeHandData.WithThumb(
                Fingers(k_Straight, k_Straight, k_Straight, k_Curled, -k_Splay, k_Splay), ThumbTucked());

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        [Test]
        public void U_WithSpreadInUnknownBand_IsUnknown()
        {
            var splay = FakeHandData.SplayForRatio(0.175f);
            var hand = FakeHandData.WithThumb(
                Fingers(k_Straight, k_Straight, k_Curled, k_Curled, -splay, splay), ThumbTucked());

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        [Test]
        public void Y_WithThumbCurled_IsUnknown()
        {
            Assert.AreEqual(HandPose.Unknown, Pose(YHand(ThumbCase.Tucked)));
        }

        [Test]
        public void Y_WithIndexExtended_IsUnknown()
        {
            var hand = FakeHandData.WithThumb(Fingers(k_Straight, k_Curled, k_Curled, k_Straight), ThumbOut());

            Assert.AreEqual(HandPose.Unknown, Pose(hand));
        }

        // --- Spec edge --------------------------------------------------------

        [Test]
        public void Pointing_WithThumbMissing_IsPointing()
        {
            // A missing thumb is Unknown, which Pointing allows ("not Extended").
            Assert.AreEqual(HandPose.Pointing, Pose(PointingHand(ThumbCase.Missing)));
        }

        // --- Resolution table -------------------------------------------------
        // The current rules are mutually exclusive, so this checks that each
        // hand resolves to its expected pose under the documented order; there
        // is no overlap between rules whose winner it could demonstrate.

        public enum FakeHand
        {
            Pointing,
            PointingThumbMissing,
            L,
            LThumbTucked,
            Y,
            V,
            VThumbOut,
            U,
            UThumbOut,
            Fist,
            Open,
        }

        static HandJointData Build(FakeHand hand)
        {
            switch (hand)
            {
                case FakeHand.Pointing: return PointingHand();
                case FakeHand.PointingThumbMissing: return PointingHand(ThumbCase.Missing);
                case FakeHand.L: return LHand();
                case FakeHand.LThumbTucked: return LHand(ThumbCase.Tucked);
                case FakeHand.Y: return YHand();
                case FakeHand.V: return VHand();
                case FakeHand.VThumbOut: return VHand(ThumbCase.Out);
                case FakeHand.U: return UHand();
                case FakeHand.UThumbOut: return UHand(ThumbCase.Out);
                case FakeHand.Fist: return FakeHandData.WithThumb(FakeHandData.Hand(k_Curled), ThumbTucked());
                default: return FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), ThumbOut());
            }
        }

        [TestCase(FakeHand.Pointing, HandPose.Pointing)]
        [TestCase(FakeHand.PointingThumbMissing, HandPose.Pointing)]
        [TestCase(FakeHand.L, HandPose.L)]
        [TestCase(FakeHand.LThumbTucked, HandPose.Pointing)]
        [TestCase(FakeHand.Y, HandPose.Y)]
        [TestCase(FakeHand.V, HandPose.V)]
        [TestCase(FakeHand.VThumbOut, HandPose.V)]   // thumb ignored
        [TestCase(FakeHand.U, HandPose.U)]
        [TestCase(FakeHand.UThumbOut, HandPose.U)]   // thumb ignored
        [TestCase(FakeHand.Fist, HandPose.Fist)]
        [TestCase(FakeHand.Open, HandPose.Open)]
        public void Resolution_EachHandResolvesToExpectedPose(FakeHand hand, HandPose expected)
        {
            Assert.AreEqual(expected, Pose(Build(hand)));
        }

        // --- Invariance -------------------------------------------------------

        [Test]
        public void RotatedAndScaled_VAndU_Unchanged()
        {
            var rotation = Quaternion.Euler(-25f, 140f, 33f);
            var translation = new Vector3(-0.7f, 1.6f, 0.3f);

            foreach (var scale in new[] { 0.75f, 1.4f })
            {
                Assert.AreEqual(HandPose.V, Pose(FakeHandData.Transformed(VHand(), rotation, translation, scale)), $"scale {scale}");
                Assert.AreEqual(HandPose.U, Pose(FakeHandData.Transformed(UHand(), rotation, translation, scale)), $"scale {scale}");
            }
        }

        // --- Spread thresholds ------------------------------------------------

        [Test]
        public void ReversedSpreadThresholds_VAndUUnknown_OpenAndFistUnchanged()
        {
            m_Settings.spreadTogetherMaxRatio = 0.3f;
            m_Settings.spreadApartMinRatio = 0.1f;

            Assert.AreEqual(HandPose.Unknown, Pose(VHand()));
            Assert.AreEqual(HandPose.Unknown, Pose(UHand()));
            Assert.AreEqual(HandPose.Open, Pose(Build(FakeHand.Open)));
            Assert.AreEqual(HandPose.Fist, Pose(Build(FakeHand.Fist)));
        }

        // --- Regression -------------------------------------------------------

        [TestCase(FakeHand.Pointing)]
        [TestCase(FakeHand.PointingThumbMissing)]
        [TestCase(FakeHand.L)]
        [TestCase(FakeHand.LThumbTucked)]
        [TestCase(FakeHand.Y)]
        [TestCase(FakeHand.V)]
        [TestCase(FakeHand.VThumbOut)]
        [TestCase(FakeHand.U)]
        [TestCase(FakeHand.UThumbOut)]
        public void Classify_NewPoseHands_StillUnknown(FakeHand hand)
        {
            // The unchanged Open/Fist classifier sees none of these as Open or Fist.
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(Build(hand), m_Settings));
        }

        [TestCase(ThumbCase.Out)]
        [TestCase(ThumbCase.Tucked)]
        [TestCase(ThumbCase.Missing)]
        public void ClassifyPose_MatchesClassify_ForFistAndOpen(ThumbCase thumb)
        {
            var fist = FakeHandData.WithThumb(FakeHandData.Hand(k_Curled), ThumbFor(thumb));
            var open = FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), ThumbFor(thumb));

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(fist, m_Settings));
            Assert.AreEqual(HandPose.Fist, Pose(fist));
            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(open, m_Settings));
            Assert.AreEqual(HandPose.Open, Pose(open));
        }

        [Test]
        public void Untracked_IsUnknown()
        {
            foreach (FakeHand fake in System.Enum.GetValues(typeof(FakeHand)))
            {
                var hand = Build(fake);
                hand.isTracked = false;
                Assert.AreEqual(HandPose.Unknown, Pose(hand), fake.ToString());
            }
        }

        [Test]
        public void NullSettings_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => HandPoseClassifier.ClassifyPose(UHand(), null));
        }
    }
}
