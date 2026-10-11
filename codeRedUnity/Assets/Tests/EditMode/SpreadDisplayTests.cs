using NUnit.Framework;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Tests for the v0.2a pose labels and the finger-pair spread debug line.
    /// Ratios come from FakeHandData.SplayedHand (see SpreadClassifierTests).
    /// </summary>
    public class SpreadDisplayTests
    {
        const float k_Curled = 80f;
        const float k_Splay = 10f;  // index-middle ratio ~0.492

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        [TestCase(HandPose.Pointing, "POINTING")]
        [TestCase(HandPose.V, "V")]
        [TestCase(HandPose.U, "U")]
        [TestCase(HandPose.Y, "Y")]
        [TestCase(HandPose.L, "L")]
        public void LabelFor_NewPoses(HandPose pose, string expected)
        {
            Assert.AreEqual(expected, HandPoseDisplay.LabelFor(pose));
        }

        [Test]
        public void DescribeSpreads_VHand_OnlyIndexMiddleEvaluated()
        {
            // Ring and little curled, so the other two pairs aren't evaluated.
            var hand = FakeHandData.SplayedHand(0f, 0f, k_Curled, k_Curled, -k_Splay, k_Splay, 0f, 0f);

            Assert.AreEqual(
                "Idx-Mid Apart (0.49) | Mid-Rng Unknown (n/a) | Rng-Lit Unknown (n/a)",
                HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }

        [Test]
        public void DescribeSpreads_ParallelFingers_AllTogetherAtZero()
        {
            var hand = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

            Assert.AreEqual(
                "Idx-Mid Together (0.00) | Mid-Rng Together (0.00) | Rng-Lit Together (0.00)",
                HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }

        [Test]
        public void DescribeSpreads_ConvergingFingers_ShowsNegativeRatio()
        {
            // Ratio ~ -0.148.
            var hand = FakeHandData.SplayedHand(0f, 0f, k_Curled, k_Curled, 3f, -3f, 0f, 0f);

            StringAssert.StartsWith("Idx-Mid Together (-0.15) |", HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }

        [Test]
        public void DescribeSpreads_TinyNegativeRatio_ShowsZeroNotMinusZero()
        {
            var splay = FakeHandData.SplayForRatio(-0.002f);
            var hand = FakeHandData.SplayedHand(0f, 0f, k_Curled, k_Curled, -splay, splay, 0f, 0f);

            StringAssert.StartsWith("Idx-Mid Together (0.00) |", HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }

        [Test]
        public void DescribeSpreads_RatioInBand_ShowsUnknownWithValue()
        {
            // A valid ratio between the thresholds is still shown, for tuning.
            var splay = FakeHandData.SplayForRatio(0.17f);
            var hand = FakeHandData.SplayedHand(0f, 0f, k_Curled, k_Curled, -splay, splay, 0f, 0f);

            StringAssert.StartsWith("Idx-Mid Unknown (0.17) |", HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }

        [Test]
        public void DescribeSpreads_Untracked_AllNotAvailable()
        {
            var hand = FakeHandData.SplayedHand(0f, 0f, 0f, 0f, -k_Splay, k_Splay, 0f, 0f);
            hand.isTracked = false;

            Assert.AreEqual(
                "Idx-Mid Unknown (n/a) | Mid-Rng Unknown (n/a) | Rng-Lit Unknown (n/a)",
                HandPoseDisplay.DescribeSpreads(hand, m_Settings));
        }
    }
}
