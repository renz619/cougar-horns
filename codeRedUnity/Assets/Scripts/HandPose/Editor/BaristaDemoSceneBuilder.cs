using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public static class BaristaDemoSceneBuilder
    {
        [MenuItem("Tools/Hand Gesture/Create Barista Demo Scene")]
        public static void Create()
        {
            const string source = "Assets/Scenes/HandPoseDemo.unity";
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/BaristaDemo.unity");
            if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException("Create the HandPoseDemo first.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                HandPoseDisplay display = null; LeftHandStateDisplay left = null; Camera camera = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (display == null) display = root.GetComponentInChildren<HandPoseDisplay>(true);
                    if (left == null) left = root.GetComponentInChildren<LeftHandStateDisplay>(true);
                    if (camera == null) camera = root.GetComponentInChildren<Camera>(true);
                }
                if (display == null || left == null || camera == null) throw new InvalidOperationException("Source needs both hands and XR camera.");
                DirectGestureDemoSetup.Ensure(scene);
                var right = (QuestHandAdapter)new SerializedObject(display).FindProperty("m_Adapter").objectReferenceValue;
                var leftAdapter = (QuestHandAdapter)new SerializedObject(left).FindProperty("m_Adapter").objectReferenceValue;
                var gestures = display.GetComponent<DirectHandGestureController>();
                var motion = new SerializedObject(gestures);
                motion.FindProperty("m_MotionSettings.thankYouStartMaximumHeight").floatValue = -.05f;
                motion.ApplyModifiedPropertiesWithoutUndo();
                // The conversation owns the visible UI; tracking and both cyan outlines continue running.
                foreach (var component in new UnityEngine.Object[] { display, left, gestures })
                {
                    var label = new SerializedObject(component).FindProperty("m_Label").objectReferenceValue as TMP_Text;
                    if (label != null) label.gameObject.SetActive(false);
                }
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.91f, .87f, .78f);
                var cafe = new GameObject("Barista Café Demo");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cafe, scene);
                var wood = Material("Walnut", new Color(.25f, .13f, .075f));
                var cream = Material("Cream", new Color(.91f, .85f, .72f));
                var green = Material("Forest", new Color(.06f, .25f, .20f));
                var skin = Material("Skin", new Color(.65f, .36f, .22f));
                var dark = Material("Espresso", new Color(.045f, .03f, .024f));
                var brass = Material("Brass", new Color(.83f, .55f, .22f));
                var white = Material("Porcelain", new Color(.97f, .96f, .9f));
                var clay = Material("Terracotta", new Color(.68f, .32f, .18f));
                var floor = Material("Floor", new Color(.66f, .57f, .45f));
                var outline = Material("Sign outlines", Color.white, "UI/Default");
                Box(cafe.transform, "Floor", new Vector3(0, -.045f, 1), new Vector3(8, .08f, 9), floor);
                Box(cafe.transform, "Back wall", new Vector3(0, 1.8f, 4), new Vector3(8, 3.6f, .12f), cream);
                Box(cafe.transform, "Left wall", new Vector3(-3.9f, 1.8f, 1), new Vector3(.12f, 3.6f, 6), green);
                Box(cafe.transform, "Right wall", new Vector3(3.9f, 1.8f, 1), new Vector3(.12f, 3.6f, 6), green);
                Box(cafe.transform, "Counter", new Vector3(0, .48f, 1.9f), new Vector3(3.3f, .96f, .75f), wood);
                Box(cafe.transform, "Countertop", new Vector3(0, 1, 1.85f), new Vector3(3.45f, .09f, .9f), cream);
                for (int i = -7; i <= 7; i++) Box(cafe.transform, "Counter slat", new Vector3(i * .21f, .48f, 1.517f), new Vector3(.025f, .85f, .025f), brass);
                Box(cafe.transform, "Back shelf", new Vector3(0, 1.25f, 3.72f), new Vector3(5.2f, .08f, .4f), wood);
                for (int i = 0; i < 6; i++)
                {
                    var jar = Primitive(cafe.transform, "Coffee jar", PrimitiveType.Cylinder, new Vector3(-2 + i * .27f, 1.43f, 3.7f), new Vector3(.15f, .16f, .15f), i % 2 == 0 ? clay : cream);
                    Primitive(jar.transform, "Lid", PrimitiveType.Cylinder, new Vector3(0, 1.02f, 0), new Vector3(1.08f, .1f, 1.08f), dark);
                }
                Box(cafe.transform, "Espresso machine", new Vector3(-1.04f, 1.24f, 2.05f), new Vector3(.56f, .43f, .35f), green);
                Box(cafe.transform, "Machine face", new Vector3(-1.04f, 1.25f, 1.864f), new Vector3(.49f, .3f, .02f), dark);
                Primitive(cafe.transform, "Machine dial", PrimitiveType.Sphere, new Vector3(-1.04f, 1.3f, 1.84f), Vector3.one * .08f, brass);
                Primitive(cafe.transform, "Plant pot", PrimitiveType.Cylinder, new Vector3(1.3f, 1.16f, 1.98f), new Vector3(.23f, .11f, .23f), clay);
                for (int i = 0; i < 5; i++)
                {
                    var leaf = Primitive(cafe.transform, "Leaf", PrimitiveType.Capsule, new Vector3(1.3f + Mathf.Sin(i * 1.25f) * .09f, 1.42f, 1.98f + Mathf.Cos(i * 1.25f) * .05f), new Vector3(.075f, .19f, .065f), green);
                    leaf.transform.localRotation = Quaternion.Euler(15 * Mathf.Cos(i), 0, 25 * Mathf.Sin(i));
                }
                var npc = new GameObject("NPC Barista"); npc.transform.SetParent(cafe.transform, false);
                npc.transform.localPosition = new Vector3(0, 0, 2.42f);
                Primitive(npc.transform, "Shirt", PrimitiveType.Capsule, new Vector3(0, 1.24f, 0), new Vector3(.48f, .3f, .3f), cream);
                Box(npc.transform, "Apron", new Vector3(0, 1.15f, -.157f), new Vector3(.34f, .52f, .025f), green);
                Box(npc.transform, "Apron pocket", new Vector3(0, 1.13f, -.18f), new Vector3(.22f, .12f, .015f), brass);
                var head = new GameObject("Barista Head"); head.transform.SetParent(npc.transform, false); head.transform.localPosition = new Vector3(0, 1.72f, 0);
                Primitive(head.transform, "Face", PrimitiveType.Sphere, Vector3.zero, new Vector3(.34f, .39f, .31f), skin);
                Primitive(head.transform, "Hair", PrimitiveType.Sphere, new Vector3(0, .13f, .045f), new Vector3(.36f, .22f, .29f), dark);
                Primitive(head.transform, "Cap", PrimitiveType.Cylinder, new Vector3(0, .19f, 0), new Vector3(.38f, .04f, .32f), green);
                Box(head.transform, "Cap brim", new Vector3(0, .16f, -.12f), new Vector3(.35f, .025f, .2f), green);
                Primitive(head.transform, "Left eye", PrimitiveType.Sphere, new Vector3(-.063f, .025f, -.145f), Vector3.one * .025f, dark);
                Primitive(head.transform, "Right eye", PrimitiveType.Sphere, new Vector3(.063f, .025f, -.145f), Vector3.one * .025f, dark);
                Primitive(head.transform, "Nose", PrimitiveType.Sphere, new Vector3(0, -.02f, -.16f), new Vector3(.04f, .05f, .035f), skin);
                for (int i = -2; i <= 2; i++) Primitive(head.transform, "Smile", PrimitiveType.Sphere, new Vector3(i * .018f, -.092f + Mathf.Abs(i) * .007f, -.139f), Vector3.one * .012f, dark);
                for (int side = -1; side <= 1; side += 2)
                {
                    var arm = Primitive(npc.transform, "Sleeve", PrimitiveType.Capsule, new Vector3(side * .31f, 1.24f, -.02f), new Vector3(.15f, .19f, .15f), cream);
                    arm.transform.localRotation = Quaternion.Euler(0, 0, side * 24);
                    Primitive(npc.transform, "Hand", PrimitiveType.Sphere, new Vector3(side * .38f, 1.06f, -.13f), new Vector3(.11f, .13f, .1f), skin);
                }
                var speech = Canvas(cafe.transform, "Barista speech", new Vector3(0, 1.36f, 1.39f), new Vector2(1050, 170), .0017f);
                Box(cafe.transform, "Speech background", new Vector3(0, 1.36f, 1.405f), new Vector3(1.84f, .32f, .012f), cream);
                var dialogue = Text(speech.transform, "Dialogue", Vector2.zero, new Vector2(1000, 140), 34, dark.color, "Welcome in! What drink can I get you?");
                var board = Canvas(cafe.transform, "Sign indicator above barista", new Vector3(0, 2.5f, 2.18f), new Vector2(1080, 500), .0022f);
                Box(cafe.transform, "Hint board", new Vector3(0, 2.5f, 2.2f), new Vector3(2.47f, 1.17f, .025f), green);
                var step = Text(board.transform, "Current step", new Vector2(0, 195), new Vector2(1020, 65), 38, white.color, "01 / HOW MANY?");
                var hint = Text(board.transform, "Instruction", new Vector2(0, -185), new Vector2(1010, 105), 29, cream.color, "Choose 1, 2, or 3. Palm toward you; hold the sign steady.");
                var groups = new GameObject[5];
                groups[0] = Group(board.transform, "Quantity hints");
                Sign(groups[0].transform, BaristaHintSign.One, -310, "1 / INDEX", outline, white.color);
                Sign(groups[0].transform, BaristaHintSign.Two, 0, "2 / INDEX + MIDDLE", outline, white.color);
                Sign(groups[0].transform, BaristaHintSign.Three, 310, "3 / THUMB + TWO", outline, white.color);
                groups[1] = Group(board.transform, "Confirm amount hint"); Sign(groups[1].transform, BaristaHintSign.Yes, 0, "YES / FIST NOD", outline, white.color);
                groups[2] = Group(board.transform, "Drink hints");
                Sign(groups[2].transform, BaristaHintSign.Coffee, -245, "COFFEE / GRIND", outline, white.color);
                Sign(groups[2].transform, BaristaHintSign.Tea, 245, "TEA / PINCH + DIP", outline, white.color);
                groups[3] = Group(board.transform, "Please hint"); Sign(groups[3].transform, BaristaHintSign.Please, 0, "PLEASE / CAMERA CIRCLE", outline, white.color);
                groups[4] = Group(board.transform, "Thank you hint"); Sign(groups[4].transform, BaristaHintSign.ThankYou, 0, "THANK YOU / BOTTOM TO OUTWARD", outline, white.color);
                for (int i = 1; i < groups.Length; i++) groups[i].SetActive(false);
                var receipt = Canvas(cafe.transform, "Order summary", new Vector3(0, .63f, 1.47f), new Vector2(1200, 230), .00165f);
                var progress = Text(receipt.transform, "Order and tracking status", Vector2.zero, new Vector2(1170, 220), 32, cream.color, "YOUR ORDER\nShow your right hand.");
                var cups = new GameObject[3];
                for (int i = 0; i < 3; i++)
                {
                    cups[i] = Group(cafe.transform, "Order cup " + (i + 1)); cups[i].transform.localPosition = new Vector3(-.32f + i * .32f, 1.1f, 1.7f);
                    Primitive(cups[i].transform, "Cup", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.17f, .065f, .17f), white);
                    Primitive(cups[i].transform, "Drink", PrimitiveType.Cylinder, new Vector3(0, .066f, 0), new Vector3(.145f, .003f, .145f), dark);
                    var handle = Primitive(cups[i].transform, "Handle", PrimitiveType.Cylinder, new Vector3(.09f, 0, 0), new Vector3(.055f, .025f, .055f), white);
                    handle.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    cups[i].SetActive(false);
                }
                var demo = cafe.AddComponent<BaristaDemoController>(); var serialized = new SerializedObject(demo);
                Assign(serialized, "m_Right", right); Assign(serialized, "m_Left", leftAdapter); Assign(serialized, "m_Gestures", gestures);
                Assign(serialized, "m_TrackingSpace", new SerializedObject(right).FindProperty("m_TrackingSpace").objectReferenceValue);
                Assign(serialized, "m_Head", camera.transform); Assign(serialized, "m_BaristaHead", head.transform);
                Assign(serialized, "m_Dialogue", dialogue); Assign(serialized, "m_Hint", hint); Assign(serialized, "m_Progress", progress); Assign(serialized, "m_Step", step);
                AssignArray(serialized, "m_HintGroups", groups); AssignArray(serialized, "m_Cups", cups);
                serialized.CopyFromSerializedProperty(new SerializedObject(display).FindProperty("m_Settings"));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.75f, .70f, .6f);
                SpiritSpeechBubbleSetup.Ensure(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
                var builds = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                builds.Add(new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = builds.ToArray();
                Debug.Log("Barista demo created: " + path + ". Open this scene and press Play. Right hand: quantity/Yes/Please/Thank You; both hands: Coffee/Tea.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        public static void RenderPreview()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BaristaDemo.unity");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var hint in root.GetComponentsInChildren<BaristaSignHint>(true)) hint.Initialize();
            EditorSceneManager.SaveScene(scene);
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
                if (camera == null) camera = root.GetComponentInChildren<Camera>(true);
            camera.transform.position = new Vector3(0, 1.6f, -.7f);
            camera.transform.LookAt(new Vector3(0, 1.7f, 2.1f));
            camera.fieldOfView = 65;
            var render = new RenderTexture(1600, 1000, 24); camera.targetTexture = render;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            void Capture(string name)
            {
                foreach (var hint in UnityEngine.Object.FindObjectsByType<BaristaSignHint>(FindObjectsSortMode.None)) hint.Render(1.1f);
                UnityEngine.Canvas.ForceUpdateCanvases();
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = render };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = render;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes("barista-" + name + ".png", image.EncodeToPNG());
            }
            BaristaDemoController demo = null;
            foreach (var root in scene.GetRootGameObjects()) if (demo == null) demo = root.GetComponentInChildren<BaristaDemoController>(true);
            void Refresh() => typeof(BaristaDemoController).GetMethod("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(demo, null);
            Capture("preview");
            demo.Order.ChooseAmount(2); Refresh(); Capture("confirm");
            demo.Order.Gesture(DirectGesture.Yes); Refresh(); Capture("drink");
            demo.Order.ChooseDrink(BaristaDrink.Coffee); Refresh(); Capture("please");
            demo.Order.Gesture(DirectGesture.Please); Refresh(); Capture("response");
            demo.Order.FinishResponse(); Refresh(); Capture("thank-you");
            demo.Order.Gesture(DirectGesture.ThankYou); Refresh(); Capture("complete");
            camera.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render);
        }

        static void Assign(SerializedObject target, string property, UnityEngine.Object value) => target.FindProperty(property).objectReferenceValue = value;
        static void AssignArray(SerializedObject target, string property, GameObject[] values)
        {
            var array = target.FindProperty(property); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        static Material Material(string name, Color color, string shaderName = "Universal Render Pipeline/Unlit")
        {
            if (!AssetDatabase.IsValidFolder("Assets/BaristaDemo")) AssetDatabase.CreateFolder("Assets", "BaristaDemo");
            if (!AssetDatabase.IsValidFolder("Assets/BaristaDemo/Materials")) AssetDatabase.CreateFolder("Assets/BaristaDemo", "Materials");
            string path = "Assets/BaristaDemo/Materials/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var material = new Material(Shader.Find(shaderName)); material.color = color;
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
            => Primitive(parent, name, PrimitiveType.Cube, position, scale, material);
        static GameObject Group(Transform parent, string name) { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        static GameObject Canvas(Transform parent, string name, Vector3 position, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = size; rect.localPosition = position; rect.localScale = Vector3.one * scale;
            return go;
        }
        static TMP_Text Text(Transform parent, string name, Vector2 position, Vector2 size, float font, Color color, string value)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>(); label.font = TMP_Settings.defaultFontAsset; label.text = value;
            label.fontSize = font; label.alignment = TextAlignmentOptions.Center; label.color = color; label.raycastTarget = false;
            label.rectTransform.sizeDelta = size; label.rectTransform.anchoredPosition = position;
            return label;
        }
        static void Sign(Transform parent, BaristaHintSign sign, float x, string caption, Material material, Color textColor)
        {
            var go = Group(parent, sign + " animated sign"); go.transform.localPosition = new Vector3(x, 0, 0);
            var hint = go.AddComponent<BaristaSignHint>(); hint.Sign = sign; hint.Material = material; hint.Initialize();
            Text(go.transform, "Caption", new Vector2(0, -117), new Vector2(sign == BaristaHintSign.ThankYou ? 900 : 440, 52), 25, textColor, caption);
        }
    }
}
