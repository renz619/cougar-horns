using UnityEditor;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public sealed class GestureRecorderWindow : EditorWindow
    {
        public const string ReviewKey = "HandPoseRecognition.PendingGestureReview.v1";
        [SerializeField] HandGestureRecorder m_Recorder;
        [SerializeField] GestureData m_Destination;
        [SerializeField] GestureCatalog m_Catalog;
        [SerializeField] GestureData m_RecordingDefinition;
        [SerializeField] RecordingSaveMode m_SaveMode;
        [SerializeField] bool m_KeepClip;
        [SerializeField] int m_First, m_Last;
        [SerializeField] bool m_ShowDepth;
        GestureRecordingReview m_Review;
        string m_Report;
        Vector2 m_Scroll;
        public HandGestureRecorder Recorder { get => m_Recorder; set => m_Recorder = value; }
        public GestureRecordingReview Review => m_Review;
        [MenuItem("Tools/Hand Gesture/Recorder Review")]
        public static void Open() => GetWindow<GestureRecorderWindow>("Gesture Recorder");
        void OnEnable()
        {
            m_Review = GestureReviewRetention.Restore(ReviewKey);
            EditorApplication.playModeStateChanged += PlayState;
            AssemblyReloadEvents.beforeAssemblyReload += Retain;
        }
        void OnDisable()
        {
            Retain(); EditorApplication.playModeStateChanged -= PlayState; AssemblyReloadEvents.beforeAssemblyReload -= Retain;
        }
        void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Retain(); }
        public void Retain()
        {
            if (m_Recorder == null) return;
            if (m_Recorder.Session.State == GestureRecordingState.Recording) m_Recorder.Stop();
            if (m_Recorder.Session.State != GestureRecordingState.ReadyForReview) return;
            // Do not overwrite an explicit trimmed review on later window/domain callbacks.
            if (m_Review == null) { m_Review = m_Recorder.Session.ExportReview(); m_Last = m_Review.CopyClip().Observations.Count - 1; }
            GestureReviewRetention.Store(ReviewKey, m_Review);
        }
        public void Discard()
        { m_Recorder?.Cancel(); m_Review = null; m_KeepClip = false; GestureReviewRetention.Clear(ReviewKey); }
        void Update()
        { if (m_Review == null && m_Recorder != null && m_Recorder.Session.State == GestureRecordingState.ReadyForReview) Retain(); Repaint(); }
        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.HelpBox("Open this window before capture. Pending whole clips survive Play Mode/domain reload while this window is used; they are lost when Unity closes. Save explicitly. No asset is written on Stop or exit.", MessageType.Info);
            using (new EditorGUI.DisabledScope(m_Review != null || m_Recorder != null && m_Recorder.Session.State == GestureRecordingState.Recording))
                m_Recorder = (HandGestureRecorder)EditorGUILayout.ObjectField("Recorder", m_Recorder, typeof(HandGestureRecorder), true);
            if (m_Recorder != null)
            {
                var serialized = new SerializedObject(m_Recorder); serialized.Update();
                using (new EditorGUI.DisabledScope(m_Recorder.Session.State == GestureRecordingState.Recording || m_Review != null))
                {
                    m_RecordingDefinition = (GestureData)EditorGUILayout.ObjectField("Copy saved definition", m_RecordingDefinition, typeof(GestureData), false);
                    if (m_RecordingDefinition != null && GUILayout.Button("Use this exact definition for the next performance"))
                    { Undo.RecordObject(m_Recorder, "Load gesture definition"); m_Recorder.Definition = m_RecordingDefinition.Definition; EditorUtility.SetDirty(m_Recorder); serialized.Update(); }
                    EditorGUILayout.PropertyField(serialized.FindProperty("m_Source"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("m_ReferenceProvider"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("m_Definition"), true); serialized.ApplyModifiedProperties();
                }
                var session = m_Recorder.Session;
                EditorGUILayout.LabelField($"{session.State} / {session.Completion}");
                EditorGUILayout.LabelField($"Frames {session.AcceptedCount}, attempts {session.AttemptCount}, rejections {session.RejectedCount}");
                if (session.LastRejectionReason != null) EditorGUILayout.HelpBox(session.LastRejectionReason, MessageType.Warning);
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || m_Review != null || session.State == GestureRecordingState.Recording))
                    if (GUILayout.Button("Begin complete labeled performance"))
                    { m_KeepClip = false; if (!m_Recorder.Begin(out m_Report)) m_Report = "Begin failed: " + m_Report; }
                using (new EditorGUI.DisabledScope(session.State != GestureRecordingState.Recording))
                    if (GUILayout.Button("Stop and review entire clip")) { m_Recorder.Stop(); Retain(); }
                if (session.State == GestureRecordingState.Recording) DrawTrail(session.CurrentClip);
            }
            if (m_Review != null)
            {
                var clip = m_Review.CopyClip(); var definition = clip.Definition;
                EditorGUILayout.LabelField($"{definition.DisplayName} ({definition.GestureId}) / dominant {definition.DominantSide}");
                EditorGUILayout.LabelField($"{clip.Observations.Count} frames, {clip.DurationSeconds:F3}s; {m_Review.AttemptCount} attempts; {m_Review.Completion}");
                EditorGUILayout.LabelField($"Epoch {clip.Reference.CoordinateEpoch}; source {clip.Reference.SourceIdentity}");
                bool valid = m_Review.TryValidate(out var result, out var error);
                EditorGUILayout.HelpBox(valid ? "Complete clip passes configured shape/motion checks. This does not establish ASL accuracy." : error, valid ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.LabelField($"Phase coverage {result.PhaseCoverage:0.00}; feature/preprocessing 1/1");
                foreach (var rejection in m_Review.CopyRejections()) EditorGUILayout.HelpBox(rejection, MessageType.Warning);
                DrawTrail(clip);
                if (clip.Observations.Count > 0)
                {
                    EditorGUILayout.LabelField($"Start {clip.StartTime:F4}s / seq {clip.Observations[0].AcquisitionId}; end {(clip.StartTime + clip.DurationSeconds):F4}s / seq {clip.Observations[clip.Observations.Count - 1].AcquisitionId}");
                    m_First = EditorGUILayout.IntField("Trim first frame (inclusive)", m_First);
                    m_Last = EditorGUILayout.IntField("Trim last frame (inclusive)", m_Last);
                    if (GUILayout.Button("Explicitly trim and fully revalidate"))
                    {
                        if (m_Review.TryTrim(m_First, m_Last, out var trimmed, out m_Report))
                        { m_Review = trimmed; m_First = 0; m_Last = trimmed.CopyClip().Observations.Count - 1; m_KeepClip = false; GestureReviewRetention.Store(ReviewKey, trimmed); }
                    }
                }
                m_KeepClip = EditorGUILayout.ToggleLeft("Keep this entire reviewed performance", m_KeepClip);
                m_SaveMode = (RecordingSaveMode)EditorGUILayout.EnumPopup("Explicit save action", m_SaveMode);
                if (m_SaveMode != RecordingSaveMode.Create) m_Destination = (GestureData)EditorGUILayout.ObjectField("Existing gesture", m_Destination, typeof(GestureData), false);
                m_Catalog = (GestureCatalog)EditorGUILayout.ObjectField("Optional saved catalog", m_Catalog, typeof(GestureCatalog), false);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !m_KeepClip || !valid))
                    if (GUILayout.Button("Save reviewed complete clip explicitly (Undo)"))
                    {
                        string path = m_SaveMode == RecordingSaveMode.Create ? EditorUtility.SaveFilePanelInProject("Save complete gesture", "RecordedGesture", "asset", "Choose Assets/HandGestures/Recorded/", "Assets/HandGestures") : null;
                        if (m_SaveMode != RecordingSaveMode.Create || !string.IsNullOrEmpty(path))
                            if (GestureRecordingPersistence.TrySave(m_Review, path, m_SaveMode == RecordingSaveMode.Create ? null : m_Destination,
                                m_SaveMode, m_Catalog, out var saved, out m_Report))
                            { m_Report = "Saved/reimported/reloaded: " + AssetDatabase.GetAssetPath(saved) + ". Explicitly rebuild the controller catalog."; Selection.activeObject = saved; }
                    }
            }
            if (GUILayout.Button("Cancel / discard pending clip")) Discard();
            if (!string.IsNullOrEmpty(m_Report)) EditorGUILayout.HelpBox(m_Report, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        void DrawTrail(GestureClip clip)
        {
            if (clip == null || clip.Observations.Count == 0) return;
            m_ShowDepth = EditorGUILayout.ToggleLeft("Trail projection: horizontal/depth (off = horizontal/vertical)", m_ShowDepth);
            var rect = GUILayoutUtility.GetRect(100, 210, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(rect, new Color(.1f, .12f, .15f));
            var points = new Vector3[clip.Observations.Count]; var definition = clip.Definition; var inverse = Quaternion.Inverse(clip.Reference.Basis);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < points.Length; i++)
            {
                var frame = clip.Observations[i].GetHand(definition.DominantSide);
                if (frame == null || !frame.TryGetJoint(definition.TrajectoryJoint, out var tip)) continue;
                var p = inverse * ((tip.Position - clip.Reference.InitialTip) / clip.Reference.PalmWidth);
                points[i] = new Vector3(p.x, m_ShowDepth ? p.z : p.y, 0); min = Vector2.Min(min, points[i]); max = Vector2.Max(max, points[i]);
            }
            var extent = max - min; float scale = Mathf.Min((rect.width - 30) / Mathf.Max(.2f, extent.x), (rect.height - 30) / Mathf.Max(.2f, extent.y));
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(rect.center.x + (points[i].x - (min.x + max.x) / 2) * scale, rect.center.y - (points[i].y - (min.y + max.y) / 2) * scale);
            Handles.BeginGUI(); Handles.color = Color.cyan; if (points.Length > 1) Handles.DrawAAPolyLine(3, points);
            Handles.color = Color.green; Handles.DrawSolidDisc(points[0], Vector3.forward, 4);
            Handles.color = Color.red; Handles.DrawSolidDisc(points[points.Length - 1], Vector3.forward, 4); Handles.EndGUI();
            EditorGUILayout.LabelField("Green = start; red = end. Both projections use the frozen reference; full 3D raw evidence is retained.", EditorStyles.wordWrappedMiniLabel);
        }
    }

    [CustomEditor(typeof(HandGestureController))]
    public sealed class HandGestureControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var controller = (HandGestureController)target;
            if (GUILayout.Button("Rebuild gesture catalog (failure makes unavailable)")) controller.RebuildCatalog();
            if (GUILayout.Button("Reload and explicitly retain previous snapshot on failure")) controller.RebuildCatalog(true);
            EditorGUILayout.HelpBox(controller.CatalogReport ?? "Not built.", MessageType.Info);
            EditorGUILayout.LabelField(controller.Session.State + ": " + controller.Session.Reason);
        }
    }
}
