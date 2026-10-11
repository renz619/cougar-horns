using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HandPoseRecognition.Editor;

namespace HandPoseRecognition.Tests
{
    public class LeftHandStateDisplayTests
    {
        [TestCase(0f, HandPose.Open, "LEFT: OPEN")]
        [TestCase(90f, HandPose.Fist, "LEFT: CLOSED")]
        [TestCase(35f, HandPose.Unknown, "LEFT: UNKNOWN")]
        public void MirroredHandUsesExistingCurlRules(float bend, HandPose expected, string text)
        {
            var hand = FakeHandData.Transformed(FakeHandData.Hand(bend), Quaternion.identity, Vector3.zero, -1);
            var result = LeftHandStateDisplay.Classify(hand, new HandPoseClassifierSettings());
            Assert.AreEqual(expected, result);
            Assert.AreEqual(text, LeftHandStateDisplay.LabelFor(result));
        }

        [Test]
        public void MissingTrackingOrJointsNeverReportsOpenOrClosed()
        {
            var hand = FakeHandData.Hand(0);
            hand.isTracked = false;
            Assert.AreEqual(HandPose.Unknown, LeftHandStateDisplay.Classify(hand, new HandPoseClassifierSettings()));
            hand.isTracked = true;
            hand.index.isValid = false;
            Assert.AreEqual(HandPose.Unknown, LeftHandStateDisplay.Classify(hand, new HandPoseClassifierSettings()));
            Assert.AreEqual(HandPose.Unknown, LeftHandStateDisplay.Classify(FakeHandData.Hand(0), null));
        }

        [Test]
        public void DisplayReadsOnlyLeftHandAndClearsOnTrackingLoss()
        {
            var go = new GameObject("Left hand test");
            go.SetActive(false);
            var labelObject = new GameObject("Label", typeof(RectTransform));
            try
            {
                var adapter = go.AddComponent<QuestHandAdapter>();
                var display = go.AddComponent<LeftHandStateDisplay>();
                var label = labelObject.AddComponent<TextMeshProUGUI>();
                var serialized = new SerializedObject(display);
                serialized.FindProperty("m_Adapter").objectReferenceValue = adapter;
                serialized.FindProperty("m_Label").objectReferenceValue = label;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var adapterSettings = new SerializedObject(adapter);
                adapterSettings.FindProperty("m_Handedness").intValue = (int)UnityEngine.XR.Hands.Handedness.Left;
                adapterSettings.ApplyModifiedPropertiesWithoutUndo();
                go.SetActive(true);
                var handField = typeof(QuestHandAdapter).GetField("m_CurrentHand", BindingFlags.Instance | BindingFlags.NonPublic);
                var update = typeof(LeftHandStateDisplay).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                handField.SetValue(adapter, FakeHandData.Hand(0));
                update.Invoke(display, null);
                Assert.AreEqual("LEFT: OPEN", label.text);
                handField.SetValue(adapter, FakeHandData.Hand(90));
                update.Invoke(display, null);
                Assert.AreEqual("LEFT: CLOSED", label.text);
                handField.SetValue(adapter, default(HandJointData));
                update.Invoke(display, null);
                Assert.AreEqual("LEFT: UNKNOWN", label.text);
                handField.SetValue(adapter, FakeHandData.Hand(0));
                adapterSettings.Update();
                adapterSettings.FindProperty("m_Handedness").intValue = (int)UnityEngine.XR.Hands.Handedness.Right;
                adapterSettings.ApplyModifiedPropertiesWithoutUndo();
                update.Invoke(display, null);
                Assert.AreEqual("LEFT: UNKNOWN", label.text, "A right-hand adapter cannot drive the left label.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(labelObject);
            }
        }

        [TestCase("HandPoseDemo")]
        [TestCase("HandPoseRecognitionDemo")]
        [TestCase("HandGestureMotionDemo")]
        public void SavedDemoHasIndependentLeftAdapterAndLabel(string sceneName)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sceneName + ".unity");
            try
            {
                LeftHandStateDisplay left = null;
                HandPoseDisplay right = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (left == null) left = root.GetComponentInChildren<LeftHandStateDisplay>(true);
                    if (right == null) right = root.GetComponentInChildren<HandPoseDisplay>(true);
                }
                Assert.IsNotNull(left);
                var serialized = new SerializedObject(left);
                var original = new SerializedObject(right);
                var adapter = (QuestHandAdapter)serialized.FindProperty("m_Adapter").objectReferenceValue;
                var label = (TMP_Text)serialized.FindProperty("m_Label").objectReferenceValue;
                Assert.AreEqual(HandSide.Left, adapter.HandSide);
                Assert.AreNotSame(original.FindProperty("m_Adapter").objectReferenceValue, adapter);
                Assert.AreNotSame(original.FindProperty("m_Label").objectReferenceValue, label);
                var leftVisualizer = left.GetComponent<HandJointVisualizer>();
                Assert.IsNotNull(leftVisualizer);
                var visualized = new SerializedObject(leftVisualizer);
                var rightVisualizer = new SerializedObject(right.GetComponent<HandJointVisualizer>());
                Assert.AreSame(adapter, visualized.FindProperty("m_Adapter").objectReferenceValue);
                foreach (string reference in new[] { "m_TrackingSpace", "m_Material" })
                {
                    Assert.IsNotNull(visualized.FindProperty(reference).objectReferenceValue);
                    Assert.AreSame(rightVisualizer.FindProperty(reference).objectReferenceValue,
                        visualized.FindProperty(reference).objectReferenceValue);
                }
                foreach (string setting in new[] { "extendedMaxCurlDegrees", "curledMinCurlDegrees" })
                    Assert.AreEqual(original.FindProperty("m_Settings." + setting).floatValue,
                        serialized.FindProperty("m_Settings." + setting).floatValue);
                LeftHandDisplaySetup.EnsureDisplay(scene);
                int count = 0;
                foreach (var root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<LeftHandStateDisplay>(true).Length;
                Assert.AreEqual(1, count, "Setup must be idempotent.");
                Assert.AreEqual(1, left.GetComponents<HandJointVisualizer>().Length);

                label.text = "LEFT: OPEN";
                typeof(LeftHandStateDisplay).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(left, null);
                Assert.AreEqual("LEFT: UNKNOWN", label.text);
                typeof(LeftHandStateDisplay).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(left, null);
                Assert.AreEqual("LEFT: UNKNOWN", label.text, "No subsystem must not leave a stale detection.");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
