using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class GestureDemoSetupTests
    {
        [Test] public void MotionDemoIsSeparateDisabledAndPreservesOriginalStaticSettings()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var original = EditorSceneManager.OpenScene("Assets/Scenes/HandPoseDemo.unity", OpenSceneMode.Additive);
            var demo = EditorSceneManager.OpenScene("Assets/Scenes/HandGestureMotionDemo.unity", OpenSceneMode.Additive);
            try
            {
                HandPoseDisplay oldDisplay = null, display = null;
                foreach (var root in original.GetRootGameObjects()) if (oldDisplay == null) oldDisplay = root.GetComponentInChildren<HandPoseDisplay>(true);
                foreach (var root in demo.GetRootGameObjects()) if (display == null) display = root.GetComponentInChildren<HandPoseDisplay>(true);
                Assert.IsNotNull(display); var controller = display.GetComponent<HandGestureController>(); var recorder = display.GetComponent<HandGestureRecorder>();
                var reference = display.GetComponent<GestureReferenceProvider>(); var debug = display.GetComponent<HandGestureDebugDisplay>();
                Assert.IsNotNull(controller); Assert.AreEqual(GestureLessonMode.Disabled, controller.Mode); Assert.AreEqual(RecognitionBackend.LegacyRules, display.selectedBackend);
                Assert.AreSame(display.GetComponent<QuestHandAdapter>(), controller.Source); Assert.AreSame(controller.Source, recorder.Source);
                Assert.AreSame(reference, controller.ReferenceProvider); Assert.AreSame(reference, recorder.ReferenceProvider);
                Assert.AreEqual("Camera Offset", reference.TrackingSpace.name); Assert.AreEqual("Main Camera", reference.Head.name);
                Assert.AreSame(controller, debug.Controller); Assert.IsNotNull(debug.Label); Assert.IsNotNull(debug.Trail); Assert.IsNotNull(debug.Trail.sharedMaterial);
                var oldSerialized = new SerializedObject(oldDisplay); var copied = new SerializedObject(display);
                Assert.AreNotSame(copied.FindProperty("m_Label").objectReferenceValue, debug.Label, "Only one writer per label.");
                foreach (string name in new[] { "extendedMaxCurlDegrees", "curledMinCurlDegrees", "thumbExtendedMinTipDistanceRatio", "thumbCurledMaxTipDistanceRatio", "spreadTogetherMaxRatio", "spreadApartMinRatio", "thumbUpMaxAngleDegrees" })
                    Assert.AreEqual(oldSerialized.FindProperty("m_Settings." + name).floatValue, copied.FindProperty("m_Settings." + name).floatValue);
                Assert.IsTrue(controller.Catalog.TryBuildSnapshot(out var catalog, out var report), report.ToString());
                Assert.AreEqual(0, catalog.Entries.Count, "No synthetic or unreviewed example is presented as real training data.");
            }
            finally { EditorSceneManager.CloseScene(demo, true); EditorSceneManager.CloseScene(original, true); }
        }
    }
}
