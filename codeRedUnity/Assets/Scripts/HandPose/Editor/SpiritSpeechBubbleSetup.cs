using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HandPoseRecognition.Editor
{
    public static class SpiritSpeechBubbleSetup
    {
        const string Folder = "Assets/SpiritSpeechBubbles";

        [MenuItem("Tools/Hand Gesture/Add Spirit Speech Bubbles")]
        public static void AddToCurrentScene()
        {
            Ensure(SceneManager.GetActiveScene());
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        public static void UpdateDemoScenes()
        {
            foreach (var name in new[] { "HandPoseDemo", "HandPoseRecognitionDemo", "HandGestureMotionDemo", "BaristaDemo" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                Ensure(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }

        public static void RenderPreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BaristaDemo.unity");
            var bubbles = UnityEngine.Object.FindFirstObjectByType<SpiritSpeechBubbles>();
            foreach (Transform child in bubbles.transform) child.gameObject.SetActive(true);
            foreach (var hint in UnityEngine.Object.FindObjectsByType<BaristaSignHint>(FindObjectsSortMode.None)) hint.Initialize();
            var camera = bubbles.GetComponentInParent<Camera>();
            camera.transform.position = new Vector3(0, 1.6f, -.7f);
            camera.transform.LookAt(new Vector3(0, 1.7f, 2.1f)); camera.fieldOfView = 65;
            var render = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            camera.targetTexture = render;
            UnityEngine.Canvas.ForceUpdateCanvases();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = render });
            RenderTexture.active = render;
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes("spirit-bubbles.png", image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render);
        }

        public static void Ensure(Scene scene)
        {
            QuestHandAdapter left = null, right = null;
            Camera camera = null;
            HandPoseDisplay display = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<SpiritSpeechBubbles>(true) != null) return;
                foreach (var adapter in root.GetComponentsInChildren<QuestHandAdapter>(true))
                    if (adapter.HandSide == HandSide.Left) left = adapter; else if (adapter.HandSide == HandSide.Right) right = adapter;
                if (camera == null) camera = root.GetComponentInChildren<Camera>(true);
                if (display == null) display = root.GetComponentInChildren<HandPoseDisplay>(true);
            }
            if (left == null || right == null || camera == null) throw new InvalidOperationException("Spirit bubbles need both hand adapters and an XR camera.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SpiritSpeechBubbles");
            var owner = new GameObject("Spirit speech bubbles");
            owner.transform.SetParent(camera.transform, false);
            var coogs = Bubble(owner.transform, "GO COOGS!", -.36f, new Color(.82f, .025f, .075f), 42);
            var horns = Bubble(owner.transform, "HOOK 'EM HORNS!", .36f, new Color(1f, .32f, .025f), 32);
            var settings = new SerializedObject(owner.AddComponent<SpiritSpeechBubbles>());
            settings.FindProperty("m_Left").objectReferenceValue = left;
            settings.FindProperty("m_Right").objectReferenceValue = right;
            settings.FindProperty("m_CoogsBubble").objectReferenceValue = coogs;
            settings.FindProperty("m_HornsBubble").objectReferenceValue = horns;
            if (display != null) settings.CopyFromSerializedProperty(new SerializedObject(display).FindProperty("m_Settings"));
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Bubble(Transform parent, string message, float x, Color color, float fontSize)
        {
            var go = new GameObject(message, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.localPosition = new Vector3(x, -.24f, .95f);
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = new Vector2(650, 150);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            string materialPath = Folder + (x < 0 ? "/CoogsRed.mat" : "/HornsOrange.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.color = color;
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var body = new GameObject("Rounded bubble and tail", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(go.transform, false);
            body.GetComponent<MeshFilter>().sharedMesh = Shape();
            body.GetComponent<MeshRenderer>().sharedMaterial = material;
            var text = new GameObject("Message", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(go.transform, false);
            text.rectTransform.sizeDelta = new Vector2(615, 135);
            text.rectTransform.localPosition = new Vector3(0, 0, -1);
            text.font = TMP_Settings.defaultFontAsset;
            text.text = message; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold;
            text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        static Mesh Shape()
        {
            string path = Folder + "/SpeechBubble.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            // Counterclockwise rounded perimeter, with a downward speech tail.
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? 293 : -293, corner < 2 ? 43 : -43);
                for (int i = 0; i <= 8; i++)
                {
                    float angle = (corner * 90 + i * 90f / 8) * Mathf.Deg2Rad;
                    vertices.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 32);
                }
                if (corner == 2) { vertices.Add(new Vector3(-105, -75)); vertices.Add(new Vector3(-100, -112)); vertices.Add(new Vector3(-60, -75)); }
            }
            for (int i = 1; i < vertices.Count; i++)
            {
                triangles.Add(0); triangles.Add(i == vertices.Count - 1 ? 1 : i + 1); triangles.Add(i);
            }
            mesh = new Mesh { name = "Rounded speech bubble", vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
