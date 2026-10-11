using NUnit.Framework;

namespace HandPoseRecognition.Tests
{
    public class HandPoseDisplayTests
    {
        // [TestCase] runs the same test once per set of arguments.
        [TestCase(HandPose.Open, "OPEN")]
        [TestCase(HandPose.Fist, "FIST")]
        [TestCase(HandPose.Unknown, "UNKNOWN")]
        public void LabelFor_MapsEachPoseToItsText(HandPose pose, string expected)
        {
            Assert.AreEqual(expected, HandPoseDisplay.LabelFor(pose));
        }

        [Test]
        public void LabelFor_UndefinedValue_ReturnsUnknown()
        {
            Assert.AreEqual("UNKNOWN", HandPoseDisplay.LabelFor((HandPose)99));
        }
    }
}
