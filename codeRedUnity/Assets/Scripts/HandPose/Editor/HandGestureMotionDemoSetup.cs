using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public static class HandGestureMotionDemoSetup
    {
        [MenuItem("Tools/Hand Gesture/Create Opt-in Motion Demo Copy")]
        public static void Create()
        {
            EnsureFolder("Assets", "HandGestures"); EnsureFolder("Assets/HandGestures", "Recorded");
            string catalogPath = AssetDatabase.GenerateUniqueAssetPath("Assets/HandGestures/Recorded/MotionCatalog.asset");
            var catalog = ScriptableObject.CreateInstance<GestureCatalog>(); AssetDatabase.CreateAsset(catalog, catalogPath);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/HandGestureMotionDemo.unity");
            if (!AssetDatabase.CopyAsset("Assets/Scenes/HandPoseDemo.unity", path)) throw new InvalidOperationException("Could not copy preserved static demo.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                HandPoseDisplay display = null; Camera head = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (display == null) display = root.GetComponentInChildren<HandPoseDisplay>(true);
                    if (head == null) head = root.GetComponentInChildren<Camera>(true);
                }
                if (display == null || head == null) throw new InvalidOperationException("Original demo has no display/XR camera.");
                var adapter = display.GetComponent<QuestHandAdapter>();
                var tracking = (Transform)new SerializedObject(adapter).FindProperty("m_TrackingSpace").objectReferenceValue;
                var reference = display.gameObject.AddComponent<GestureReferenceProvider>(); reference.TrackingSpace = tracking; reference.Head = head.transform;
                var recorder = display.gameObject.AddComponent<HandGestureRecorder>(); recorder.Source = adapter; recorder.ReferenceProvider = reference;
                var controller = display.gameObject.AddComponent<HandGestureController>(); controller.Source = adapter; controller.ReferenceProvider = reference;
                controller.Catalog = catalog; controller.Mode = GestureLessonMode.Disabled;
                var oldLabel = (TMP_Text)new SerializedObject(display).FindProperty("m_Label").objectReferenceValue;
                var labelObject = UnityEngine.Object.Instantiate(oldLabel.gameObject, oldLabel.transform.parent); labelObject.name = "Motion Debug Label";
                var rect = labelObject.GetComponent<RectTransform>();
                rect.anchoredPosition += new Vector2(0, -rect.rect.height - 30);
                var label = labelObject.GetComponent<TMP_Text>(); label.text = "MOTION DISABLED"; label.fontSize = Mathf.Min(oldLabel.fontSize, 24);
                var trailObject = new GameObject("Complete motion trail"); trailObject.transform.SetParent(display.transform, false);
                var trail = trailObject.AddComponent<LineRenderer>(); trail.positionCount = 0; trail.widthMultiplier = .004f;
                trail.startColor = Color.green; trail.endColor = Color.red; trail.numCapVertices = 4;
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    string materialPath = AssetDatabase.GenerateUniqueAssetPath("Assets/HandGestures/MotionTrail.mat");
                    var material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); trail.sharedMaterial = material;
                }
                var debug = display.gameObject.AddComponent<HandGestureDebugDisplay>(); debug.Controller = controller; debug.Label = label; debug.Trail = trail;
                EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
                Debug.Log("Motion demo saved: " + path + "; default Disabled; empty catalog " + catalogPath + ". Record reviewed complete examples before enabling JZOnly.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        static void EnsureFolder(string parent, string child)
        { if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child); }
    }
}
