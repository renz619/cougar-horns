using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>Tests for the per-finger judgement used by debug readouts.</summary>
    public class FingerStateClassifierTests
    {
        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        static FingerJoints Finger(float bendPerJointDeg) =>
            FakeHandData.Finger(bendPerJointDeg, Quaternion.identity, Vector3.zero);

        // Per-joint bend -> total curl (3x) against the defaults 60 / 160.
        [TestCase(5f, FingerState.Extended)]   // 15
        [TestCase(19f, FingerState.Extended)]  // 57, just inside the threshold
        [TestCase(35f, FingerState.Unknown)]   // 105, between thresholds
        [TestCase(80f, FingerState.Curled)]    // 240
        public void ClassifyFinger_MatchesThresholds(float bendPerJointDeg, FingerState expected)
        {
            Assert.AreEqual(expected, HandPoseClassifier.ClassifyFinger(Finger(bendPerJointDeg), m_Settings));
        }

        [Test]
        public void ClassifyFinger_InvalidFinger_ReturnsUnknown()
        {
            var finger = Finger(80f);
            finger.isValid = false;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyFinger(finger, m_Settings));
        }

        [Test]
        public void ClassifyFinger_ReversedThresholds_ReturnsUnknown()
        {
            m_Settings.extendedMaxCurlDegrees = 200f;
            m_Settings.curledMinCurlDegrees = 100f;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyFinger(Finger(5f), m_Settings));
            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyFinger(Finger(80f), m_Settings));
        }

        [Test]
        public void ClassifyFinger_AgreesWithClassify()
        {
            var fist = FakeHandData.Hand(80f);
            var open = FakeHandData.Hand(5f);

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(fist, m_Settings));
            Assert.AreEqual(FingerState.Curled, HandPoseClassifier.ClassifyFinger(fist.index, m_Settings));
            Assert.AreEqual(FingerState.Curled, HandPoseClassifier.ClassifyFinger(fist.little, m_Settings));

            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(open, m_Settings));
            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyFinger(open.middle, m_Settings));
            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyFinger(open.ring, m_Settings));
        }
    }
}
