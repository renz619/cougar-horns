using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Tests for the finger spread measure. Expected ratios come from
    /// FakeHandData.SplayedHand geometry (palm width 0.06 m, 0.085 m from
    /// knuckle to tip) and were recomputed independently. Defaults: together
    /// &lt;= 0.10, apart &gt;= 0.25.
    /// </summary>
    public class SpreadClassifierTests
    {
        const float k_Curled = 80f;  // 240 degrees total curl
        const float k_Splay = 10f;   // index -10, middle +10: ratio ~0.492

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        // Index and middle straight with the given splays; ring and little curled.
        static HandJointData IndexMiddle(float indexSplay, float middleSplay, float middleBend = 0f) =>
            FakeHandData.SplayedHand(0f, middleBend, k_Curled, k_Curled, indexSplay, middleSplay, 0f, 0f);

        float Ratio(in HandJointData hand, FingerPair pair = FingerPair.IndexMiddle) =>
            HandPoseClassifier.FingerSpreadRatio(hand, pair, m_Settings);

        SpreadState State(in HandJointData hand, FingerPair pair = FingerPair.IndexMiddle) =>
            HandPoseClassifier.ClassifySpread(hand, pair, m_Settings);

        // --- Formula baselines ---------------------------------------------

        [Test]
        public void ParallelFingers_RatioZero_Together()
        {
            var hand = IndexMiddle(0f, 0f);

            Assert.AreEqual(0f, Ratio(hand), 1e-5f);
            Assert.AreEqual(SpreadState.Together, State(hand));
        }

        [Test]
        public void ConvergingFingers_RatioNegative_Together()
        {
            // Tips tilted toward each other: ratio ~ -0.148.
            var hand = IndexMiddle(3f, -3f);

            Assert.Less(Ratio(hand), 0f);
            Assert.AreEqual(-0.148f, Ratio(hand), 0.002f);
            Assert.AreEqual(SpreadState.Together, State(hand));
        }

        [Test]
        public void SplayedFingers_RatioPositive_Apart()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);

            Assert.AreEqual(0.492f, Ratio(hand), 0.002f);
            Assert.AreEqual(SpreadState.Apart, State(hand));
        }

        // --- Differential flexion --------------------------------------------

        // Middle bends 17 degrees per joint (51 total, ~54 with the splay) and
        // still counts as Extended; index stays straight.
        const float k_MiddleBend = 17f;

        [Test]
        public void DifferentialFlexion_Together_StaysTogether()
        {
            var hand = IndexMiddle(0f, 0f, k_MiddleBend);

            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyFinger(hand.middle, m_Settings));
            // Flexion here is perpendicular to the knuckle line, so it doesn't register.
            Assert.AreEqual(0f, Ratio(hand), 1e-5f);
            Assert.AreEqual(SpreadState.Together, State(hand));
        }

        [Test]
        public void DifferentialFlexion_Apart_StaysApart()
        {
            var bent = IndexMiddle(-k_Splay, k_Splay, k_MiddleBend);
            var straight = IndexMiddle(-k_Splay, k_Splay);

            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyFinger(bent.middle, m_Settings));
            // Bending shortens the splayed finger's reach: ~0.492 -> ~0.453.
            // Reduced sensitivity, not none, and still well above 0.25.
            Assert.AreEqual(0.453f, Ratio(bent), 0.002f);
            Assert.Less(Ratio(bent), Ratio(straight));
            Assert.AreEqual(SpreadState.Apart, State(bent));
        }

        // --- V/U boundary ------------------------------------------------------

        [TestCase(0.09f, SpreadState.Together)]
        [TestCase(0.11f, SpreadState.Unknown)]
        [TestCase(0.175f, SpreadState.Unknown)]
        [TestCase(0.24f, SpreadState.Unknown)]
        [TestCase(0.26f, SpreadState.Apart)]
        public void Boundary_AroundThresholds(float targetRatio, SpreadState expected)
        {
            var splay = FakeHandData.SplayForRatio(targetRatio);
            var hand = IndexMiddle(-splay, splay);

            Assert.AreEqual(targetRatio, Ratio(hand), 1e-4f);
            Assert.AreEqual(expected, State(hand));
        }

        // --- Exact thresholds (inclusive comparisons) -------------------------

        [Test]
        public void RatioEqualToTogetherThreshold_IsTogether()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);
            var ratio = Ratio(hand);
            m_Settings.spreadTogetherMaxRatio = ratio;
            m_Settings.spreadApartMinRatio = ratio + 0.1f;

            Assert.AreEqual(SpreadState.Together, State(hand));
        }

        [Test]
        public void RatioEqualToApartThreshold_IsApart()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);
            var ratio = Ratio(hand);
            m_Settings.spreadTogetherMaxRatio = ratio - 0.1f;
            m_Settings.spreadApartMinRatio = ratio;

            Assert.AreEqual(SpreadState.Apart, State(hand));
        }

        // --- Threshold validation -----------------------------------------------

        [TestCase(0.2f, 0.2f)]                                       // equal
        [TestCase(0.3f, 0.1f)]                                       // reversed
        [TestCase(float.NaN, 0.25f)]
        [TestCase(0.10f, float.NaN)]
        [TestCase(0.10f, float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity, 0.25f)]
        [TestCase(float.NegativeInfinity, float.PositiveInfinity)]
        public void InvalidThresholds_ReturnUnknown(float together, float apart)
        {
            m_Settings.spreadTogetherMaxRatio = together;
            m_Settings.spreadApartMinRatio = apart;

            Assert.AreEqual(SpreadState.Unknown, State(IndexMiddle(0f, 0f)));
            Assert.AreEqual(SpreadState.Unknown, State(IndexMiddle(-k_Splay, k_Splay)));
        }

        // --- Known geometry ---------------------------------------------------

        static FingerJoints StraightFinger(float x, float tipX)
        {
            return new FingerJoints
            {
                metacarpal = new Vector3(x, 0f, -0.05f),
                proximal = new Vector3(x, 0f, 0f),
                intermediate = new Vector3(x, 0f, 0.04f),
                distal = new Vector3(x, 0f, 0.07f),
                tip = new Vector3(tipX, 0f, 0.09f),
                isValid = true,
            };
        }

        [Test]
        public void KnownGeometry_ExactRatio()
        {
            // Palm width 0.06. Middle tip moved 0.03 sideways (distal bend ~56
            // degrees, still Extended): tips 0.05 apart vs knuckles 0.02 apart,
            // so (0.05 - 0.02) / 0.06 = 0.5.
            var hand = new HandJointData
            {
                isTracked = true,
                index = StraightFinger(0f, 0f),
                middle = StraightFinger(0.02f, 0.05f),
                ring = StraightFinger(0.04f, 0.04f),
                little = StraightFinger(0.06f, 0.06f),
            };

            Assert.AreEqual(0.5f, Ratio(hand), 1e-5f);
        }

        // --- Invariance -------------------------------------------------------

        [Test]
        public void RotatedTranslatedScaled_SameStateAndRatio()
        {
            var rotation = Quaternion.Euler(37f, -120f, 65f);
            var translation = new Vector3(1.3f, -0.4f, 2.2f);
            const float scale = 1.4f;

            var together = IndexMiddle(0f, 0f);
            var apart = IndexMiddle(-k_Splay, k_Splay);
            var movedTogether = FakeHandData.Transformed(together, rotation, translation, scale);
            var movedApart = FakeHandData.Transformed(apart, rotation, translation, scale);

            Assert.AreEqual(SpreadState.Together, State(movedTogether));
            Assert.AreEqual(SpreadState.Apart, State(movedApart));
            Assert.AreEqual(Ratio(together), Ratio(movedTogether), 1e-4f);
            Assert.AreEqual(Ratio(apart), Ratio(movedApart), 1e-4f);
        }

        // --- Other pairs ------------------------------------------------------

        [Test]
        public void MiddleRing_TogetherAndApart()
        {
            var together = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            var apart = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, 0f, -k_Splay, k_Splay, 0f);

            Assert.AreEqual(SpreadState.Together, State(together, FingerPair.MiddleRing));
            Assert.AreEqual(SpreadState.Apart, State(apart, FingerPair.MiddleRing));
        }

        [Test]
        public void RingLittle_TogetherAndApart()
        {
            var together = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            var apart = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, 0f, 0f, -k_Splay, k_Splay);

            Assert.AreEqual(SpreadState.Together, State(together, FingerPair.RingLittle));
            Assert.AreEqual(SpreadState.Apart, State(apart, FingerPair.RingLittle));
        }

        // --- Not both extended ------------------------------------------------

        [Test]
        public void MiddleCurled_NaNAndUnknown()
        {
            var hand = FakeHandData.SplayedHand(0f, k_Curled, k_Curled, k_Curled, -k_Splay, k_Splay, 0f, 0f);

            Assert.IsNaN(Ratio(hand));
            Assert.AreEqual(SpreadState.Unknown, State(hand));
        }

        [Test]
        public void MiddleInCurlBand_Unknown()
        {
            // 35 per joint = 105 total, between 60 and 160.
            var hand = FakeHandData.SplayedHand(0f, 35f, k_Curled, k_Curled, 0f, 0f, 0f, 0f);

            Assert.IsNaN(Ratio(hand));
            Assert.AreEqual(SpreadState.Unknown, State(hand));
        }

        // --- Invalid data -----------------------------------------------------

        static HandJointData AllStraightApart() =>
            FakeHandData.SplayedHand(0f, 0f, 0f, 0f, -k_Splay, k_Splay, -k_Splay, k_Splay);

        void AssertAllPairsUnknown(in HandJointData hand)
        {
            foreach (FingerPair pair in System.Enum.GetValues(typeof(FingerPair)))
            {
                Assert.IsNaN(Ratio(hand, pair), pair.ToString());
                Assert.AreEqual(SpreadState.Unknown, State(hand, pair), pair.ToString());
            }
        }

        [Test]
        public void Untracked_AllPairsUnknown()
        {
            var hand = AllStraightApart();
            hand.isTracked = false;

            AssertAllPairsUnknown(hand);
        }

        [Test]
        public void IndexInvalid_AllPairsUnknown()
        {
            // The index knuckle defines the knuckle line, so even ring-little fails.
            var hand = AllStraightApart();
            hand.index.isValid = false;

            AssertAllPairsUnknown(hand);
        }

        [Test]
        public void LittleInvalid_AllPairsUnknown()
        {
            var hand = AllStraightApart();
            hand.little.isValid = false;

            AssertAllPairsUnknown(hand);
        }

        [Test]
        public void NaNTip_Unknown()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);
            hand.middle.tip = new Vector3(float.NaN, 0f, 0f);

            Assert.IsNaN(Ratio(hand));
            Assert.AreEqual(SpreadState.Unknown, State(hand));
        }

        [Test]
        public void InfiniteTip_Unknown()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);
            hand.middle.tip = new Vector3(float.PositiveInfinity, 0f, 0f);

            Assert.IsNaN(Ratio(hand));
            Assert.AreEqual(SpreadState.Unknown, State(hand));
        }

        [Test]
        public void CoincidentKnuckles_PalmWidthZero_Unknown()
        {
            var hand = IndexMiddle(-k_Splay, k_Splay);
            hand.little.proximal = hand.index.proximal;

            Assert.IsNaN(Ratio(hand));
            Assert.AreEqual(SpreadState.Unknown, State(hand));
        }

        [Test]
        public void NullSettings_Throws()
        {
            var hand = IndexMiddle(0f, 0f);

            Assert.Throws<System.ArgumentNullException>(() => HandPoseClassifier.FingerSpreadRatio(hand, FingerPair.IndexMiddle, null));
            Assert.Throws<System.ArgumentNullException>(() => HandPoseClassifier.ClassifySpread(hand, FingerPair.IndexMiddle, null));
        }
    }
}
