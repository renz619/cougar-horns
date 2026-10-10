using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// EditMode tests run inside the Editor without entering Play mode, so no
    /// scene or headset is needed. Run them from Window > General > Test Runner.
    /// </summary>
    public class HandPoseClassifierTests
    {
        // Per-joint bends. Total curl is 3x each value; compare it with the
        // default thresholds (extended <= 60, curled >= 160).
        const float k_FistBend = 80f;       // 240 total
        const float k_OpenBend = 5f;        // 15 total
        const float k_AmbiguousBend = 35f;  // 105 total, inside the gap

        HandPoseClassifierSettings m_Settings;

        // [SetUp] runs before every [Test], giving each test fresh default settings.
        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        [Test]
        public void AllFingersCurled_ReturnsFist()
        {
            var hand = FakeHandData.Hand(k_FistBend);

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void AllFingersExtended_ReturnsOpen()
        {
            var hand = FakeHandData.Hand(k_OpenBend);

            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void PartialPose_TwoExtendedTwoCurled_ReturnsUnknown()
        {
            var hand = FakeHandData.Hand(
                k_OpenBend, k_OpenBend, k_FistBend, k_FistBend,
                Quaternion.identity, Vector3.zero);

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void AmbiguousCurl_ReturnsUnknown()
        {
            var hand = FakeHandData.Hand(k_AmbiguousBend);

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void NotTracked_ReturnsUnknown()
        {
            var hand = FakeHandData.Hand(k_FistBend);
            hand.isTracked = false;

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void OneFingerInvalid_ReturnsUnknown()
        {
            var hand = FakeHandData.Hand(k_FistBend);
            hand.ring.isValid = false;

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void DefaultData_ReturnsUnknown()
        {
            var hand = default(HandJointData);

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));

            // Even when flagged tracked and valid, all-zero joints are
            // degenerate and must not be read as a gesture.
            hand.isTracked = true;
            hand.index.isValid = true;
            hand.middle.isValid = true;
            hand.ring.isValid = true;
            hand.little.isValid = true;

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void NaNJoint_ReturnsUnknown()
        {
            var hand = FakeHandData.Hand(k_FistBend);
            hand.middle.distal = new Vector3(float.NaN, 0f, 0f);

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(hand, m_Settings));
        }

        [Test]
        public void RotatedAndTranslatedHand_ClassificationUnchanged()
        {
            var orientation = Quaternion.Euler(37f, -120f, 65f);
            var origin = new Vector3(1.3f, -0.4f, 2.2f);

            var fist = FakeHandData.Hand(k_FistBend, k_FistBend, k_FistBend, k_FistBend, orientation, origin);
            var open = FakeHandData.Hand(k_OpenBend, k_OpenBend, k_OpenBend, k_OpenBend, orientation, origin);

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(fist, m_Settings));
            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(open, m_Settings));
        }

        [Test]
        public void InvalidThresholds_ReturnsUnknown()
        {
            m_Settings.extendedMaxCurlDegrees = 200f;
            m_Settings.curledMinCurlDegrees = 100f;

            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(FakeHandData.Hand(k_FistBend), m_Settings));
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(FakeHandData.Hand(k_OpenBend), m_Settings));
        }
    }
}
