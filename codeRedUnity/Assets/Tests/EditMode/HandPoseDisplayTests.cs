using NUnit.Framework;
using System.Reflection;
using TMPro;
using UnityEngine;

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

        [Test]
        public void NoSubsystem_ShowsTrackingExplanationOnFirstRefresh()
        {
            WithDisplay((adapter, display, label) =>
            {
                Assert.AreEqual(HandPose.Unknown, display.currentPose);
                StringAssert.Contains("UNKNOWN", label.text);
                StringAssert.Contains("Waiting for Quest hand tracking", label.text);
            });
        }

        [Test]
        public void DisablingAdapter_UpdatesExplanationWhilePoseRemainsUnknown()
        {
            WithDisplay((adapter, display, label) =>
            {
                adapter.enabled = false;
                Invoke(display, "Update");

                Assert.AreEqual(HandPose.Unknown, display.currentPose);
                StringAssert.Contains("Hand reader is disabled", label.text);
                StringAssert.DoesNotContain("Waiting for Quest", label.text);
            });
        }

        [Test]
        public void MissingAdapter_ShowsSetupExplanationWithoutThrowing()
        {
            WithDisplay((adapter, display, label) =>
            {
                SetField(display, "m_Adapter", null);
                Invoke(display, "Update");

                Assert.AreEqual(HandPose.Unknown, display.currentPose);
                StringAssert.Contains("Hand reader is not connected", label.text);
            });
        }

        static void WithDisplay(System.Action<QuestHandAdapter, HandPoseDisplay, TMP_Text> check)
        {
            var owner = new GameObject("Display test");
            owner.SetActive(false);
            var labelObject = new GameObject("Label", typeof(RectTransform));
            try
            {
                var adapter = owner.AddComponent<QuestHandAdapter>();
                var display = owner.AddComponent<HandPoseDisplay>();
                var label = labelObject.AddComponent<TextMeshProUGUI>();
                label.text = "Previous text";
                SetField(display, "m_Adapter", adapter);
                SetField(display, "m_Label", label);
                owner.SetActive(true);
                // EditMode does not run ordinary MonoBehaviour callbacks.
                Invoke(display, "OnEnable");
                check(adapter, display, label);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(labelObject);
            }
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
