using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class SpiritSpeechBubbleTests
    {
        static HandJointData Pose(bool coogs)
            => FakeHandData.WithThumb(FakeHandData.Hand(0, coogs ? 0 : 90, 90, 0, Quaternion.identity, Vector3.zero), FakeHandData.Thumb(0, 0));

        [TestCase(true, SpiritPose.GoCoogs)]
        [TestCase(false, SpiritPose.HookEmHorns)]
        public void ExactPoseWorksForEitherMirroredHand(bool coogs, SpiritPose expected)
        {
            var hand = Pose(coogs);
            Assert.AreEqual(expected, SpiritSpeechBubbles.Classify(hand, new HandPoseClassifierSettings()));
            Assert.AreEqual(expected, SpiritSpeechBubbles.Classify(FakeHandData.Transformed(hand, Quaternion.Euler(25, 40, 75), Vector3.one, -1), new HandPoseClassifierSettings()));
        }

        [Test]
        public void EveryDigitIsRequiredAndTrackingLossClearsPose()
        {
            for (int digit = 0; digit < 5; digit++)
            {
                var hand = Pose(true);
                if (digit == 0) hand.thumb.isValid = false;
                if (digit == 1) hand.index.isValid = false;
                if (digit == 2) hand.middle.isValid = false;
                if (digit == 3) hand.ring.isValid = false;
                if (digit == 4) hand.little.isValid = false;
                Assert.AreEqual(SpiritPose.None, SpiritSpeechBubbles.Classify(hand, new HandPoseClassifierSettings()));
            }
            var wrongThumb = FakeHandData.WithThumb(Pose(true), FakeHandData.Thumb(90, 0));
            Assert.AreEqual(SpiritPose.None, SpiritSpeechBubbles.Classify(wrongThumb, new HandPoseClassifierSettings()));
            var missing = Pose(true); missing.isTracked = false;
            Assert.AreEqual(SpiritPose.None, SpiritSpeechBubbles.Classify(missing, new HandPoseClassifierSettings()));
            Assert.AreEqual(SpiritPose.None, SpiritSpeechBubbles.Classify(Pose(true), null));
            Assert.AreEqual(SpiritPose.None, SpiritSpeechBubbles.Classify(FakeHandData.Hand(0), new HandPoseClassifierSettings()));
        }

        [Test]
        public void BubblesFollowBothHandsDisappearAndReappearWithoutLatching()
        {
            var owner = new GameObject("test"); owner.SetActive(false);
            var coogs = new GameObject("red"); var horns = new GameObject("orange");
            var leftObject = new GameObject("left"); var rightObject = new GameObject("right");
            try
            {
                leftObject.SetActive(false); rightObject.SetActive(false);
                var left = leftObject.AddComponent<QuestHandAdapter>(); var right = rightObject.AddComponent<QuestHandAdapter>();
                var leftSettings = new SerializedObject(left);
                leftSettings.FindProperty("m_Handedness").intValue = (int)UnityEngine.XR.Hands.Handedness.Left;
                leftSettings.ApplyModifiedPropertiesWithoutUndo();
                var rightSettings = new SerializedObject(right);
                rightSettings.FindProperty("m_Handedness").intValue = (int)UnityEngine.XR.Hands.Handedness.Right;
                rightSettings.ApplyModifiedPropertiesWithoutUndo();
                var bubbles = owner.AddComponent<SpiritSpeechBubbles>();
                var settings = new SerializedObject(bubbles);
                settings.FindProperty("m_Left").objectReferenceValue = left; settings.FindProperty("m_Right").objectReferenceValue = right;
                settings.FindProperty("m_CoogsBubble").objectReferenceValue = coogs; settings.FindProperty("m_HornsBubble").objectReferenceValue = horns;
                settings.ApplyModifiedPropertiesWithoutUndo();
                leftObject.SetActive(true); rightObject.SetActive(true); owner.SetActive(true);
                var field = typeof(QuestHandAdapter).GetField("m_CurrentHand", BindingFlags.Instance | BindingFlags.NonPublic);
                var refresh = typeof(SpiritSpeechBubbles).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                void Check(HandJointData l, HandJointData r, bool c, bool h)
                {
                    field.SetValue(left, l); field.SetValue(right, r); refresh.Invoke(bubbles, null);
                    Assert.AreEqual(c, coogs.activeSelf); Assert.AreEqual(h, horns.activeSelf);
                }
                Check(Pose(true), Pose(false), true, true);
                Check(default, Pose(false), false, true);
                Check(Pose(false), Pose(true), true, true);
                Check(Pose(true), Pose(true), true, false);
                Check(default, default, false, false);
                Check(default, Pose(true), true, false);
                right.enabled = false; refresh.Invoke(bubbles, null);
                Assert.IsFalse(coogs.activeSelf);
                Check(Pose(false), default, false, true);
                bubbles.enabled = false;
                // EditMode does not run MonoBehaviour lifecycle callbacks automatically.
                typeof(SpiritSpeechBubbles).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bubbles, null);
                Assert.IsFalse(coogs.activeSelf); Assert.IsFalse(horns.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(owner); Object.DestroyImmediate(coogs); Object.DestroyImmediate(horns);
                Object.DestroyImmediate(leftObject); Object.DestroyImmediate(rightObject);
            }
        }

        [TestCase("HandPoseDemo")]
        [TestCase("HandPoseRecognitionDemo")]
        [TestCase("HandGestureMotionDemo")]
        [TestCase("BaristaDemo")]
        public void SavedSceneHasBothBubblesAndBothAdapters(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
            try
            {
                SpiritSpeechBubbles component = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (component == null) component = root.GetComponentInChildren<SpiritSpeechBubbles>(true);
                Assert.IsNotNull(component);
                var s = new SerializedObject(component);
                foreach (var property in new[] { "m_Left", "m_Right", "m_CoogsBubble", "m_HornsBubble" })
                    Assert.IsNotNull(s.FindProperty(property).objectReferenceValue);
                var coogs = (GameObject)s.FindProperty("m_CoogsBubble").objectReferenceValue;
                var horns = (GameObject)s.FindProperty("m_HornsBubble").objectReferenceValue;
                Assert.IsFalse(coogs.activeSelf); Assert.IsFalse(horns.activeSelf);
                Assert.AreEqual("GO COOGS!", coogs.GetComponentInChildren<TMPro.TMP_Text>(true).text);
                Assert.AreEqual("HOOK 'EM HORNS!", horns.GetComponentInChildren<TMPro.TMP_Text>(true).text);
                Assert.AreNotSame(coogs, horns);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
