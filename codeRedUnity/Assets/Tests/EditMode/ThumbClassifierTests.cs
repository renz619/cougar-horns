using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class ThumbClassifierTests
    {
        // Thumb shapes from FakeHandData.Thumb (heading, bend per joint) and the
        // tip-distance ratio each produces, with the default thresholds
        // (extended >= 0.60, curled <= 0.45).
        const float k_OutHeading = 45f;     // straight, angled away: ratio ~0.90
        const float k_TuckedBend = 60f;     // heading 0, curled toward index: ratio ~0.33
        const float k_BetweenHeading = 15f; // straight, close to the index: ratio ~0.56

        // Finger bends reused from the Open/Fist tests: 15 and 240 degrees total.
        const float k_OpenBend = 5f;
        const float k_FistBend = 80f;

        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        static ThumbJoints OutThumb() => FakeHandData.Thumb(k_OutHeading, 0f);
        static ThumbJoints TuckedThumb() => FakeHandData.Thumb(0f, k_TuckedBend);

        static HandJointData HandWith(ThumbJoints thumb) =>
            FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), thumb);

        [Test]
        public void ThumbExtended_ReturnsExtended()
        {
            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyThumb(HandWith(OutThumb()), m_Settings));
        }

        [Test]
        public void ThumbTucked_ReturnsCurled()
        {
            Assert.AreEqual(FingerState.Curled, HandPoseClassifier.ClassifyThumb(HandWith(TuckedThumb()), m_Settings));
        }

        [Test]
        public void ThumbBetweenThresholds_ReturnsUnknown()
        {
            var hand = HandWith(FakeHandData.Thumb(k_BetweenHeading, 0f));

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void ThumbMissing_ReturnsUnknown()
        {
            var hand = HandWith(OutThumb());
            hand.thumb.isValid = false;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void HandNotTracked_ThumbUnknown()
        {
            var hand = HandWith(OutThumb());
            hand.isTracked = false;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void ThumbNaNJoint_ReturnsUnknown()
        {
            var hand = HandWith(OutThumb());
            hand.thumb.tip = new Vector3(float.NaN, 0f, 0f);

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void ThumbCoincidentJoints_ReturnsUnknown()
        {
            var hand = HandWith(OutThumb());
            hand.thumb.distal = hand.thumb.proximal;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void IndexInvalid_ThumbUnknown()
        {
            var hand = HandWith(OutThumb());
            hand.index.isValid = false;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(hand, m_Settings));
        }

        [Test]
        public void ThumbRotatedTranslatedScaled_StateUnchanged()
        {
            var rotation = Quaternion.Euler(37f, -120f, 65f);
            var translation = new Vector3(1.3f, -0.4f, 2.2f);
            const float scale = 1.4f;

            var outHand = HandWith(OutThumb());
            var tuckedHand = HandWith(TuckedThumb());
            var movedOut = FakeHandData.Transformed(outHand, rotation, translation, scale);
            var movedTucked = FakeHandData.Transformed(tuckedHand, rotation, translation, scale);

            Assert.AreEqual(FingerState.Extended, HandPoseClassifier.ClassifyThumb(movedOut, m_Settings));
            Assert.AreEqual(FingerState.Curled, HandPoseClassifier.ClassifyThumb(movedTucked, m_Settings));
            Assert.AreEqual(
                HandPoseClassifier.ThumbTipDistanceRatio(outHand.thumb, outHand.index),
                HandPoseClassifier.ThumbTipDistanceRatio(movedOut.thumb, movedOut.index),
                1e-4f);
        }

        [Test]
        public void ReversedThumbThresholds_ReturnsUnknown()
        {
            m_Settings.thumbExtendedMinTipDistanceRatio = 0.3f;
            m_Settings.thumbCurledMaxTipDistanceRatio = 0.8f;

            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(HandWith(OutThumb()), m_Settings));
            Assert.AreEqual(FingerState.Unknown, HandPoseClassifier.ClassifyThumb(HandWith(TuckedThumb()), m_Settings));
        }

        [Test]
        public void ThumbTipDistanceRatio_KnownGeometry()
        {
            // Straight thumb along +Z, length 0.04 + 0.03 + 0.03 = 0.10 m.
            var thumb = new ThumbJoints
            {
                metacarpal = new Vector3(0f, 0f, 0f),
                proximal = new Vector3(0f, 0f, 0.04f),
                distal = new Vector3(0f, 0f, 0.07f),
                tip = new Vector3(0f, 0f, 0.10f),
                isValid = true,
            };
            // Knuckle 0.03 to the side and 0.04 behind the tip: distance 0.05 m.
            var index = new FingerJoints { proximal = new Vector3(0.03f, 0f, 0.06f), isValid = true };

            Assert.AreEqual(0.5f, HandPoseClassifier.ThumbTipDistanceRatio(thumb, index), 1e-5f);
        }

        public enum ThumbCase
        {
            Extended,
            Tucked,
            Missing,
            NaN,
        }

        static ThumbJoints ThumbFor(ThumbCase thumbCase)
        {
            switch (thumbCase)
            {
                case ThumbCase.Extended:
                    return OutThumb();
                case ThumbCase.Tucked:
                    return TuckedThumb();
                case ThumbCase.Missing:
                    return default;
                default:
                    var thumb = OutThumb();
                    thumb.tip = new Vector3(float.NaN, float.NaN, float.NaN);
                    return thumb;
            }
        }

        [TestCase(ThumbCase.Extended)]
        [TestCase(ThumbCase.Tucked)]
        [TestCase(ThumbCase.Missing)]
        [TestCase(ThumbCase.NaN)]
        public void FistAndOpen_IgnoreThumb(ThumbCase thumbCase)
        {
            var fist = FakeHandData.WithThumb(FakeHandData.Hand(k_FistBend), ThumbFor(thumbCase));
            var open = FakeHandData.WithThumb(FakeHandData.Hand(k_OpenBend), ThumbFor(thumbCase));

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(fist, m_Settings));
            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(open, m_Settings));
        }

        [Test]
        public void ReversedThumbThresholds_DoNotAffectClassify()
        {
            m_Settings.thumbExtendedMinTipDistanceRatio = 0.3f;
            m_Settings.thumbCurledMaxTipDistanceRatio = 0.8f;

            Assert.AreEqual(HandPose.Fist, HandPoseClassifier.Classify(FakeHandData.Hand(k_FistBend), m_Settings));
            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(FakeHandData.Hand(k_OpenBend), m_Settings));
        }
    }
}
