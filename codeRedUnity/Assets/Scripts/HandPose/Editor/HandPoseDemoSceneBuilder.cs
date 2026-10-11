using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace HandPoseRecognition.Editor
{
    /// <summary>
    /// Editor-only tool that creates the v0.1 demo scene and makes it the
    /// startup scene in Build Settings. Run it from Tools > Hand Pose >
    /// Create Demo Scene, or in batch mode with
    /// -executeMethod HandPoseRecognition.Editor.HandPoseDemoSceneBuilder.CreateDemoScene.
    /// </summary>
    /// <remarks>
    /// Scripts in an Editor-only assembly are never included in builds; this
    /// only automates setup that could also be done by hand in the Editor.
    /// The XR Origin mirrors the one in the XR Hands HandVisualizer sample.
    /// </remarks>
    static class HandPoseDemoSceneBuilder
    {
        const string k_ScenePath = "Assets/Scenes/HandPoseDemo.unity";

        // Same as the HandVisualizer sample: the seated/standing eye height used
        // when the runtime doesn't report a floor-relative origin.
        const float k_CameraYOffset = 1.1176f;

        [MenuItem("Tools/Hand Pose/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            // TextMeshPro needs its Essential Resources (default font and
            // settings) imported into Assets, or the label renders nothing.
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                Debug.LogError("TextMeshPro Essential Resources are missing. Import them with Window > TextMeshPro > Import TMP Essential Resources, then run Tools > Hand Pose > Create Demo Scene again.");
                return;
            }

            if (!Application.isBatchMode)
            {
                if (File.Exists(k_ScenePath)
                    && !EditorUtility.DisplayDialog("Create Hand Pose Demo Scene", $"{k_ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                    return;

                // Give the user a chance to save the scene that is currently open.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLight();
            var camera = CreateXROrigin();
            var label = CreateLabel(camera);
            CreateRecognition(label);

            if (!EditorSceneManager.SaveScene(scene, k_ScenePath))
            {
                Debug.LogError($"Could not save {k_ScenePath}.");
                return;
            }

            SetAsStartupScene();
            Debug.Log($"Created {k_ScenePath} and set it as the startup scene in Build Settings.");
        }

        static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static Camera CreateXROrigin()
        {
            // XR Origin is the root of the tracked rig. Head and hand poses are
            // reported relative to it.
            var originObject = new GameObject("XR Origin");

            var offsetObject = new GameObject("Camera Offset");
            offsetObject.transform.SetParent(originObject.transform, false);
            offsetObject.transform.localPosition = new Vector3(0f, k_CameraYOffset, 0f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offsetObject.transform, false);

            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            cameraObject.AddComponent<AudioListener>();

            // Moves the camera with the headset. These are the same bindings the
            // HandVisualizer sample uses.
            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(CreateAction("Position", "Vector3", "<XRHMD>/centerEyePosition"));
            poseDriver.rotationInput = new InputActionProperty(CreateAction("Rotation", "Quaternion", "<XRHMD>/centerEyeRotation"));

            var origin = originObject.AddComponent<XROrigin>();
            origin.Origin = originObject;
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.NotSpecified;
            origin.CameraYOffset = k_CameraYOffset;

            return camera;
        }

        static InputAction CreateAction(string name, string controlType, string bindingPath)
        {
            var action = new InputAction(name, expectedControlType: controlType);
            action.AddBinding(bindingPath);
            return action;
        }

        static TMP_Text CreateLabel(Camera camera)
        {
            // A world-space canvas parented to the camera stays 1.5 m in front
            // of the user's eyes wherever they look.
            var canvasObject = new GameObject("Pose Label Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(camera.transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;

            // 800 x 300 canvas units at 0.001 scale = 0.8 m x 0.3 m.
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(800f, 300f);
            canvasRect.localPosition = new Vector3(0f, -0.1f, 1.5f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.001f;

            var labelObject = new GameObject("Pose Label", typeof(RectTransform));
            labelObject.transform.SetParent(canvasObject.transform, false);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = HandPoseDisplay.LabelFor(HandPose.Unknown);
            label.fontSize = 150f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            // Stretch the text to fill the canvas.
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return label;
        }

        static void CreateRecognition(TMP_Text label)
        {
            var recognitionObject = new GameObject("Hand Pose Recognition");
            var adapter = recognitionObject.AddComponent<QuestHandAdapter>();
            var display = recognitionObject.AddComponent<HandPoseDisplay>();

            // The display's fields are private [SerializeField]s, so assign them
            // through SerializedObject, the same path the Inspector uses.
            var serializedDisplay = new SerializedObject(display);
            serializedDisplay.FindProperty("m_Adapter").objectReferenceValue = adapter;
            serializedDisplay.FindProperty("m_Label").objectReferenceValue = label;
            serializedDisplay.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetAsStartupScene()
        {
            // The first enabled scene is the one a build launches. Other scenes
            // stay listed but are disabled so they aren't built.
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(k_ScenePath, true) };
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path != k_ScenePath)
                    scenes.Add(new EditorBuildSettingsScene(existing.path, false));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
