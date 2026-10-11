using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public sealed class PoseRecorderWindow : EditorWindow
    {
        const string ReviewKey = "HandPoseRecognition.PendingReview.v1";
        [SerializeField] HandPoseRecorder m_Recorder;
        [SerializeField] HandPoseData m_Destination;
        [SerializeField] PoseDatabase m_Catalog;
        [SerializeField] RecordingSaveMode m_SaveMode;
        [SerializeField] List<int> m_Selection = new List<int>();
        PoseRecordingReview m_Review;
        string m_Report;
        Vector2 m_Scroll;

        [MenuItem("Tools/Hand Pose/Recorder Review")]
        public static void Open() => GetWindow<PoseRecorderWindow>("Pose Recorder");
        void OnEnable()
        {
            m_Review = PoseReviewRetention.Restore(ReviewKey);
            EditorApplication.playModeStateChanged += PlayState;
            AssemblyReloadEvents.beforeAssemblyReload += Retain;
        }
        void OnDisable()
        {
            Retain();
            EditorApplication.playModeStateChanged -= PlayState;
            AssemblyReloadEvents.beforeAssemblyReload -= Retain;
        }
        void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Retain();
        }
        void Retain()
        {
            if (m_Recorder == null) return;
            if (m_Recorder.Session.State == RecordingState.Recording) m_Recorder.Stop();
            if (m_Recorder.Session.State != RecordingState.ReadyForReview) return;
            m_Review = m_Recorder.Session.ExportReview();
            PoseReviewRetention.Store(ReviewKey, m_Review);
        }
        void Update()
        {
            if (m_Recorder != null && m_Recorder.Session.State == RecordingState.ReadyForReview && m_Review == null) Retain();
            Repaint();
        }
        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.HelpBox("Pending review survives domain reload and Play Mode exit while this window is open. It does not survive closing Unity. Save explicitly before closing Unity. Stop/exit never writes a template asset.", MessageType.Info);
            using (new EditorGUI.DisabledScope(m_Review != null || (m_Recorder != null && m_Recorder.Session.State == RecordingState.Recording)))
                m_Recorder = (HandPoseRecorder)EditorGUILayout.ObjectField("Recorder", m_Recorder, typeof(HandPoseRecorder), true);
            if (m_Recorder != null)
            {
                var serialized = new SerializedObject(m_Recorder);
                serialized.Update();
                using (new EditorGUI.DisabledScope(m_Recorder.Session.State == RecordingState.Recording))
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("m_Source"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("m_Settings"), true);
                    serialized.ApplyModifiedProperties();
                }
                var session = m_Recorder.Session;
                EditorGUILayout.LabelField($"{session.State}: {session.Completion}");
                EditorGUILayout.LabelField($"Accepted {session.AcceptedCount}, rejected {session.RejectedCount}, attempts {session.AttemptCount}");
                EditorGUILayout.LabelField("Last rejection", session.LastRejectionReason ?? "None");
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || session.State == RecordingState.Recording || m_Review != null))
                    if (GUILayout.Button("Begin labeled capture"))
                    {
                        m_Selection.Clear();
                        if (!m_Recorder.Begin(out m_Report)) Repaint();
                    }
                using (new EditorGUI.DisabledScope(session.State != RecordingState.Recording))
                    if (GUILayout.Button("Stop and retain review")) { m_Recorder.Stop(); Retain(); }
                if (GUILayout.Button("Cancel / discard pending review"))
                {
                    m_Recorder.Cancel(); m_Review = null; m_Selection.Clear(); PoseReviewRetention.Clear(ReviewKey);
                }
            }
            if (m_Review != null)
            {
                var samples = m_Review.CopySamples();
                var definition = m_Review.CopyDefinition();
                EditorGUILayout.LabelField($"Frozen: {definition.PoseId} / {definition.DisplayName} / {definition.Side}");
                EditorGUILayout.LabelField("Choose representative samples explicitly:");
                for (int i = 0; i < samples.Length; i++)
                {
                    bool keep = m_Selection.Contains(i);
                    bool next = EditorGUILayout.ToggleLeft($"Keep {i}: sequence {samples[i].SourceSequenceId}, time {samples[i].SourceTimestampSeconds:F4}s", keep);
                    if (next != keep) { if (next) m_Selection.Add(i); else m_Selection.Remove(i); }
                }
                m_SaveMode = (RecordingSaveMode)EditorGUILayout.EnumPopup("Explicit save action", m_SaveMode);
                if (m_SaveMode != RecordingSaveMode.Create)
                    m_Destination = (HandPoseData)EditorGUILayout.ObjectField("Existing template", m_Destination, typeof(HandPoseData), false);
                m_Catalog = (PoseDatabase)EditorGUILayout.ObjectField("Optional catalog update", m_Catalog, typeof(PoseDatabase), false);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || m_Selection.Count == 0))
                    if (GUILayout.Button("Save selected samples explicitly (Undo supported)"))
                    {
                        string path = m_SaveMode == RecordingSaveMode.Create
                            ? EditorUtility.SaveFilePanelInProject("Create reviewed template", "RecordedShape", "asset", "Choose a folder under Assets/HandPoses/", "Assets/HandPoses") : null;
                        if (m_SaveMode != RecordingSaveMode.Create || !string.IsNullOrEmpty(path))
                        {
                            m_Selection.Sort();
                            if (PoseRecordingPersistence.TrySave(m_Review, m_Selection.ToArray(), path,
                                m_SaveMode == RecordingSaveMode.Create ? null : m_Destination, m_SaveMode, m_Catalog, out var saved, out m_Report))
                            { m_Report = "Saved, reimported and validated " + AssetDatabase.GetAssetPath(saved) + ". Explicitly rebuild the controller snapshot."; Selection.activeObject = saved; }
                        }
                    }
                if (GUILayout.Button("Discard retained review"))
                { m_Review = null; m_Selection.Clear(); PoseReviewRetention.Clear(ReviewKey); if (m_Recorder != null) m_Recorder.Cancel(); }
            }
            if (!string.IsNullOrEmpty(m_Report)) EditorGUILayout.HelpBox(m_Report, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
    }

    [CustomEditor(typeof(RecognitionController))]
    public sealed class RecognitionControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (RecognitionController)target;
            if (GUILayout.Button("Rebuild catalog (failure makes unavailable)")) controller.RebuildCatalog();
            if (GUILayout.Button("Reload, explicitly retaining previous snapshot on failure")) controller.RebuildCatalog(true);
            EditorGUILayout.LabelField("Catalog", controller.CatalogReport ?? "Not built");
            EditorGUILayout.LabelField($"{controller.Session.Status}: {controller.Session.Reason}");
        }
    }

    public static class HandPoseRecognitionDemoSetup
    {
        [MenuItem("Tools/Hand Pose/Create Opt-in Recognition Demo Copy")]
        public static void Create()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/HandPoseRecognitionDemo.unity");
            if (!AssetDatabase.CopyAsset("Assets/Scenes/HandPoseDemo.unity", path))
                throw new System.InvalidOperationException("Could not copy the preserved demo.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var display in root.GetComponentsInChildren<HandPoseDisplay>(true))
                    {
                        var adapter = display.GetComponent<QuestHandAdapter>();
                        var controller = display.gameObject.AddComponent<RecognitionController>();
                        controller.Source = adapter;
                        controller.Database = AssetDatabase.LoadAssetAtPath<PoseDatabase>("Assets/HandPoses/Synthetic/SyntheticDatabase.asset");
                        controller.Backend = RecognitionBackend.LegacyRules;
                        display.controller = controller;
                        var recorder = display.gameObject.AddComponent<HandPoseRecorder>();
                        recorder.Source = adapter;
                        EditorUtility.SetDirty(display); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(recorder);
                    }
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Opt-in recognition demo copied and wired: " + path + "; default LegacyRules; supplied database is synthetic only.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
