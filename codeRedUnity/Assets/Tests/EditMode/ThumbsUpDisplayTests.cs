using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>Tests for the THUMBS UP label and the thumb-direction debug line.</summary>
    public class ThumbsUpDisplayTests
    {
        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        static HandJointData HandAt(float angleFromUp)
        {
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(80f), FakeHandData.Thumb(45f, 0f));
            return FakeHandData.WithTrackingSpace(hand, FakeHandData.ThumbUpRotation(hand, angleFromUp));
        }

        [Test]
        public void LabelFor_ThumbsUp()
        {
            Assert.AreEqual("THUMBS UP", HandPoseDisplay.LabelFor(HandPose.ThumbsUp));
        }

        [Test]
        public void DescribeOrientation_Up_ShowsAngle()
        {
            Assert.AreEqual("Thumb Dir Up (20.00°)", HandPoseDisplay.DescribeOrientation(HandAt(20f), m_Settings));
        }

        [Test]
        public void DescribeOrientation_Sideways_ShowsNotUp()
        {
            Assert.AreEqual("Thumb Dir NotUp (90.00°)", HandPoseDisplay.DescribeOrientation(HandAt(90f), m_Settings));
        }

        [Test]
        public void DescribeOrientation_NoTrackingSpace_NotAvailable()
        {
            var hand = HandAt(0f);
            hand.hasTrackingSpaceToWorld = false;

            Assert.AreEqual("Thumb Dir Unknown (n/a)", HandPoseDisplay.DescribeOrientation(hand, m_Settings));
        }

        [Test]
        public void DescribeOrientation_Untracked_NotAvailable()
        {
            var hand = HandAt(0f);
            hand.isTracked = false;

            Assert.AreEqual("Thumb Dir Unknown (n/a)", HandPoseDisplay.DescribeOrientation(hand, m_Settings));
        }

        [Test]
        public void DescribeOrientation_InvalidSetting_ShowsUnknownWithAngle()
        {
            // A valid angle is still shown so the value can be read while the
            // setting is fixed.
            m_Settings.thumbUpMaxAngleDegrees = 90f;

            Assert.AreEqual("Thumb Dir Unknown (20.00°)", HandPoseDisplay.DescribeOrientation(HandAt(20f), m_Settings));
        }
    }
}
