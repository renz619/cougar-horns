using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HandPoseRecognition.Editor
{
    public static class DirectGestureDemoSetup
    {
        [MenuItem("Tools/Hand Gesture/Use Direct Thank You Please Yes Rules")]
        public static void AddToActiveScene() => Ensure(SceneManager.GetActiveScene());

        public static void Ensure(Scene scene)
        {
            HandPoseDisplay display = null;
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<HandPoseDisplay>(true))
                {
                    var adapter = new SerializedObject(candidate).FindProperty("m_Adapter").objectReferenceValue as QuestHandAdapter;
                    if (adapter != null && adapter.HandSide == HandSide.Right) display = candidate;
                }
                foreach (var candidate in root.GetComponentsInChildren<Camera>(true))
                    if (candidate.CompareTag("MainCamera")) camera = candidate;
            }
            if (display == null || camera == null) throw new InvalidOperationException("Scene needs a right-hand display and tracked Main Camera.");
            var source = new SerializedObject(display);
            var hand = (QuestHandAdapter)source.FindProperty("m_Adapter").objectReferenceValue;
            var controller = display.GetComponent<DirectHandGestureController>();
            if (controller == null) controller = display.gameObject.AddComponent<DirectHandGestureController>();
            var serialized = new SerializedObject(controller);
            var label = serialized.FindProperty("m_Label").objectReferenceValue as TMP_Text;
            if (label == null)
            {
                var old = (TMP_Text)source.FindProperty("m_Label").objectReferenceValue;
                var obj = UnityEngine.Object.Instantiate(old.gameObject, old.transform.parent);
                obj.name = "Direct Gesture Label";
                label = obj.GetComponent<TMP_Text>();
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                label.rectTransform.sizeDelta = new Vector2(900, 150);
                label.rectTransform.anchoredPosition = new Vector2(0, -250);
                label.fontSize = 44; label.enableAutoSizing = false;
                label.alignment = TextAlignmentOptions.Center; label.color = Color.cyan;
                label.raycastTarget = false; label.text = "GESTURES: UNKNOWN";
            }
            serialized.FindProperty("m_Label").objectReferenceValue = label;
            serialized.FindProperty("m_Adapter").objectReferenceValue = hand;
            serialized.FindProperty("m_Head").objectReferenceValue = camera.transform;
            serialized.FindProperty("m_TrackingSpace").objectReferenceValue =
                new SerializedObject(hand).FindProperty("m_TrackingSpace").objectReferenceValue;
            serialized.CopyFromSerializedProperty(source.FindProperty("m_Settings"));
            serialized.ApplyModifiedPropertiesWithoutUndo(); controller.enabled = true;

            // Switch the scene's active gesture path to the direct rules.
            // Keep recorded assets available for any future return to J/Z.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var old in root.GetComponentsInChildren<HandGestureController>(true))
                { old.Mode = GestureLessonMode.Disabled; old.enabled = false; }
                foreach (var debug in root.GetComponentsInChildren<HandGestureDebugDisplay>(true))
                { debug.enabled = false; if (debug.Label != null) debug.Label.gameObject.SetActive(false); if (debug.Trail != null) debug.Trail.gameObject.SetActive(false); }
                foreach (var feedback in root.GetComponentsInChildren<HandGestureDetectionDisplay>(true))
                { feedback.enabled = false; if (feedback.Label != null) feedback.Label.gameObject.SetActive(false); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void UpdateDemoScenes()
        {
            foreach (string name in new[] { "HandPoseDemo", "HandPoseRecognitionDemo", "HandGestureMotionDemo" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                Ensure(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + name);
            }
        }
    }
}
