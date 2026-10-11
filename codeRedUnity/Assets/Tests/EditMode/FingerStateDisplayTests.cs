using NUnit.Framework;

namespace HandPoseRecognition.Tests
{
    /// <summary>Tests for the five-finger debug line shown under the pose.</summary>
    public class FingerStateDisplayTests
    {
        const float k_OpenBend = 5f;
        const float k_FistBend = 80f;

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        [Test]
        public void DescribeFingerStates_ListsAllFive()
        {
            // Fist with the thumb out: ratio ~0.906.
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(k_FistBend), FakeHandData.Thumb(45f, 0f));

            Assert.AreEqual(
                "Thumb Extended (0.91) | Index Curled (240.00°) | Middle Curled (240.00°) | Ring Curled (240.00°) | Little Curled (240.00°)",
                HandPoseDisplay.DescribeFingerStates(hand, m_Settings));
        }

        [Test]
        public void DescribeFingerStates_TuckedThumb_ShowsCurledWithRatio()
        {
            // Open fingers with the thumb tucked: ratio ~0.330.
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), FakeHandData.Thumb(0f, 60f));

            Assert.AreEqual(
                "Thumb Curled (0.33) | Index Extended (15.00°) | Middle Extended (15.00°) | Ring Extended (15.00°) | Little Extended (15.00°)",
                HandPoseDisplay.DescribeFingerStates(hand, m_Settings));
        }

        [Test]
        public void DescribeFingerStates_ThumbBetweenThresholds_ShowsUnknownWithRatio()
        {
            // A valid ratio in the gap (~0.561) is still shown, so it can be tuned.
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), FakeHandData.Thumb(15f, 0f));

            StringAssert.StartsWith("Thumb Unknown (0.56) |", HandPoseDisplay.DescribeFingerStates(hand, m_Settings));
        }

        [Test]
        public void DescribeFingerStates_ThumbMissing_ShowsNotAvailable()
        {
            // Hand(...) leaves the thumb unset, so it is invalid.
            var hand = FakeHandData.Hand(k_FistBend);

            Assert.AreEqual(
                "Thumb Unknown (n/a) | Index Curled (240.00°) | Middle Curled (240.00°) | Ring Curled (240.00°) | Little Curled (240.00°)",
                HandPoseDisplay.DescribeFingerStates(hand, m_Settings));
        }

        [Test]
        public void DescribeFingerStates_Untracked_AllUnknown()
        {
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(k_FistBend), FakeHandData.Thumb(45f, 0f));
            hand.isTracked = false;

            Assert.AreEqual(
                "Thumb Unknown (n/a) | Index Unknown (n/a) | Middle Unknown (n/a) | Ring Unknown (n/a) | Little Unknown (n/a)",
                HandPoseDisplay.DescribeFingerStates(hand, m_Settings));
        }
    }
}
