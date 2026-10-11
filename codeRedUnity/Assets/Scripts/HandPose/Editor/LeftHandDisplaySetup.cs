using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition.Editor
{
    public static class LeftHandDisplaySetup
    {
        [MenuItem("Tools/Hand Pose/Add Left Hand Display to Active Scene")]
        public static void AddToActiveScene()
        {
            EnsureDisplay(SceneManager.GetActiveScene());
        }

        public static void EnsureDisplay(Scene scene)
        {
            HandPoseDisplay right = null;
            LeftHandStateDisplay left = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (left == null) left = root.GetComponentInChildren<LeftHandStateDisplay>(true);
                foreach (var display in root.GetComponentsInChildren<HandPoseDisplay>(true))
                {
                    var source = new SerializedObject(display).FindProperty("m_Adapter").objectReferenceValue as QuestHandAdapter;
                    if (source != null && source.HandSide == HandSide.Right) right = display;
                }
            }
            if (right == null) throw new InvalidOperationException("Scene needs a right-hand HandPoseDisplay first.");
            if (left != null)
            {
                EnsureVisualizer(left, right);
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }
            var original = new SerializedObject(right);
            var rightAdapter = (QuestHandAdapter)original.FindProperty("m_Adapter").objectReferenceValue;
            var rightLabel = (TMP_Text)original.FindProperty("m_Label").objectReferenceValue;
            if (rightLabel == null) throw new InvalidOperationException("Right-hand display needs a label.");

            var go = new GameObject("Left Hand State");
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, scene);
            var adapter = go.AddComponent<QuestHandAdapter>();
            var adapterSettings = new SerializedObject(adapter);
            adapterSettings.FindProperty("m_Handedness").intValue = (int)Handedness.Left;
            adapterSettings.FindProperty("m_TrackingSpace").objectReferenceValue =
                new SerializedObject(rightAdapter).FindProperty("m_TrackingSpace").objectReferenceValue;
            adapterSettings.ApplyModifiedPropertiesWithoutUndo();

            var labelObject = UnityEngine.Object.Instantiate(rightLabel.gameObject, rightLabel.transform.parent);
            labelObject.name = "Left Hand State Label";
            var label = labelObject.GetComponent<TMP_Text>();
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(800, 80);
            rect.anchoredPosition = new Vector2(0, 310);
            label.text = "LEFT: UNKNOWN";
            label.fontSize = 48;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var state = go.AddComponent<LeftHandStateDisplay>();
            var serialized = new SerializedObject(state);
            serialized.FindProperty("m_Adapter").objectReferenceValue = adapter;
            serialized.FindProperty("m_Label").objectReferenceValue = label;
            serialized.CopyFromSerializedProperty(original.FindProperty("m_Settings"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EnsureVisualizer(state, right);
            go.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void EnsureVisualizer(LeftHandStateDisplay left, HandPoseDisplay right)
        {
            var source = right.GetComponent<HandJointVisualizer>();
            if (source == null) throw new InvalidOperationException("Right hand needs a HandJointVisualizer to copy.");
            var visualizer = left.GetComponent<HandJointVisualizer>();
            if (visualizer == null) visualizer = left.gameObject.AddComponent<HandJointVisualizer>();
            var original = new SerializedObject(source);
            var serialized = new SerializedObject(visualizer);
            serialized.FindProperty("m_Adapter").objectReferenceValue =
                new SerializedObject(left).FindProperty("m_Adapter").objectReferenceValue;
            serialized.CopyFromSerializedProperty(original.FindProperty("m_TrackingSpace"));
            serialized.CopyFromSerializedProperty(original.FindProperty("m_Material"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Also callable in an isolated batch editor to update the saved demos.
        public static void UpdateDemoScenes()
        {
            foreach (string name in new[] { "HandPoseDemo", "HandPoseRecognitionDemo", "HandGestureMotionDemo" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                EnsureDisplay(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + name);
            }
        }
    }
}
