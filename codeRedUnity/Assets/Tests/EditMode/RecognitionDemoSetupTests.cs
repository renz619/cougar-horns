using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition.Tests
{
    public class RecognitionDemoSetupTests
    {
        static HandPoseDisplay Display(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var display = root.GetComponentInChildren<HandPoseDisplay>(true);
                if (display != null) return display;
            }
            Assert.Fail("Scene must contain the preserved display."); return null;
        }
        [Test] public void OptInScenePreservesTuningSideRigAndLegacyDefaultAndAddsWiring()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var original = EditorSceneManager.OpenScene("Assets/Scenes/HandPoseDemo.unity", OpenSceneMode.Additive);
            var variant = EditorSceneManager.OpenScene("Assets/Scenes/HandPoseRecognitionDemo.unity", OpenSceneMode.Additive);
            try
            {
                var oldDisplay = Display(original); var newDisplay = Display(variant);
                Assert.IsNull(oldDisplay.controller); Assert.AreEqual(RecognitionBackend.LegacyRules, oldDisplay.selectedBackend);
                Assert.IsNotNull(newDisplay.controller); Assert.AreEqual(RecognitionBackend.LegacyRules, newDisplay.controller.Backend);
                Assert.AreEqual(RecognitionBackend.LegacyRules, newDisplay.selectedBackend);
                var adapter = newDisplay.GetComponent<QuestHandAdapter>(); Assert.AreEqual(Handedness.Right, adapter.handedness);
                Assert.AreSame(adapter, newDisplay.controller.Source); Assert.IsNotNull(newDisplay.controller.Database);
                Assert.AreSame(adapter, newDisplay.GetComponent<HandPoseRecorder>().Source);
                var oldSerialized = new SerializedObject(oldDisplay); var newSerialized = new SerializedObject(newDisplay);
                foreach (string setting in new[] { "extendedMaxCurlDegrees", "curledMinCurlDegrees", "thumbExtendedMinTipDistanceRatio",
                    "thumbCurledMaxTipDistanceRatio", "spreadTogetherMaxRatio", "spreadApartMinRatio", "thumbUpMaxAngleDegrees" })
                    Assert.AreEqual(oldSerialized.FindProperty("m_Settings." + setting).floatValue,
                        newSerialized.FindProperty("m_Settings." + setting).floatValue, setting);
                var trackingSpace = (Transform)new SerializedObject(adapter).FindProperty("m_TrackingSpace").objectReferenceValue;
                Assert.IsNotNull(trackingSpace); Assert.AreEqual("Camera Offset", trackingSpace.name);
                Assert.IsTrue(newDisplay.controller.Database.TryBuildSnapshot(out var snapshot, out var report), report.ToString());
                Assert.IsTrue(snapshot.TryGetPose("synthetic.fixture.right", out _));
            }
            finally { EditorSceneManager.CloseScene(variant, true); EditorSceneManager.CloseScene(original, true); }
        }
    }
}
