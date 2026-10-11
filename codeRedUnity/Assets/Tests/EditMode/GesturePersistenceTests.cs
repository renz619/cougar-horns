using System;
using System.Collections;
using HandPoseRecognition.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace HandPoseRecognition.Tests
{
    public class GesturePersistenceTests
    {
        string m_Folder;
        [SetUp] public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/HandGestures")) AssetDatabase.CreateFolder("Assets", "HandGestures");
            string leaf = "GestureTest_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets/HandGestures", leaf); m_Folder = "Assets/HandGestures/" + leaf;
        }
        [TearDown] public void TearDown() { Undo.ClearAll(); AssetDatabase.DeleteAsset(m_Folder); }
        GestureCatalog Catalog()
        { var catalog = ScriptableObject.CreateInstance<GestureCatalog>(); AssetDatabase.CreateAsset(catalog, m_Folder + "/Catalog.asset"); return catalog; }
        [Test] public void CreateUnloadReloadAppendReplaceAndUndoPreserveIdentityAndSnapshot()
        {
            var d = GestureTestFixtures.Definition(); d.MaxClips = 2; var review = GestureRecordingTests.Review(d); var catalog = Catalog();
            Assert.IsTrue(GestureRecordingPersistence.TrySave(review, m_Folder + "/Gesture.asset", null, RecordingSaveMode.Create, catalog, out var saved, out var error), error);
            Assert.IsTrue(catalog.TryBuildSnapshot(out var oldSnapshot, out _)); string path = AssetDatabase.GetAssetPath(saved);
            Resources.UnloadAsset(saved); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); saved = AssetDatabase.LoadAssetAtPath<GestureData>(path);
            Assert.IsNotNull(saved); Assert.IsTrue(saved.TryValidate(out var report), report.ToString());
            Assert.AreEqual(review.CopyClip().Observations[10].Right.SequenceId, saved.CopyClips()[0].Observations[10].Right.SequenceId);
            Assert.IsTrue(GestureRecordingPersistence.TrySave(review, null, saved, RecordingSaveMode.Append, catalog, out saved, out error), error);
            Assert.AreEqual(2, saved.CopyClips().Length); Assert.AreEqual(1, oldSnapshot.Entries[0].Clips.Count);
            Assert.IsFalse(GestureRecordingPersistence.TrySave(review, null, saved, RecordingSaveMode.Append, catalog, out _, out _)); Assert.AreEqual(2, saved.CopyClips().Length);
            Assert.IsTrue(GestureRecordingPersistence.TrySave(review, null, saved, RecordingSaveMode.Replace, catalog, out saved, out error), error);
            Assert.AreEqual(1, saved.CopyClips().Length); Undo.PerformUndo(); Assert.AreEqual(2, saved.CopyClips().Length);
            Assert.AreEqual(d.GestureId, saved.Definition.GestureId);
        }
        [Test] public void DuplicateCatalogAndTransientCatalogFailBeforeWrites()
        {
            var review = GestureRecordingTests.Review(); var catalog = Catalog();
            Assert.IsTrue(GestureRecordingPersistence.TrySave(review, m_Folder + "/First.asset", null, RecordingSaveMode.Create, catalog, out var saved, out var error), error);
            Assert.IsFalse(GestureRecordingPersistence.TrySave(review, m_Folder + "/Duplicate.asset", null, RecordingSaveMode.Create, catalog, out _, out _));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GestureData>(m_Folder + "/Duplicate.asset")); Assert.AreEqual(1, catalog.CopyEntries().Length);
            Assert.AreSame(saved, catalog.CopyEntries()[0]);
            var transient = ScriptableObject.CreateInstance<GestureCatalog>();
            try { Assert.IsFalse(GestureRecordingPersistence.TrySave(review, m_Folder + "/Transient.asset", null, RecordingSaveMode.Create, transient, out _, out _)); Assert.AreEqual(0, transient.CopyEntries().Length); }
            finally { UnityEngine.Object.DestroyImmediate(transient); }
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GestureData>(m_Folder + "/Transient.asset"));
        }
        [Test] public void IncompatibleDefinitionCannotReplaceSavedIdentity()
        {
            var review = GestureRecordingTests.Review();
            Assert.IsTrue(GestureRecordingPersistence.TrySave(review, m_Folder + "/Gesture.asset", null, RecordingSaveMode.Create, null, out var saved, out var error), error);
            var d = GestureTestFixtures.Definition(); d.GestureId = "different";
            Assert.IsFalse(GestureRecordingPersistence.TrySave(GestureRecordingTests.Review(d), null, saved, RecordingSaveMode.Replace, null, out _, out _));
            Assert.AreEqual("synthetic.motion.Z", saved.Definition.GestureId);
        }
        [UnityTest] public IEnumerator ActualRecorderWindowRetainsReviewAcrossPlayModeExitAndReopen()
        {
            GestureReviewRetention.Clear(GestureRecorderWindow.ReviewKey);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var owner = new GameObject("Test-owned motion recorder retention");
            var recorder = owner.AddComponent<HandGestureRecorder>();
            // A hidden real EditorWindow exercises its hooks without a graphics device in batchmode.
            var window = ScriptableObject.CreateInstance<GestureRecorderWindow>(); window.Discard(); window.Recorder = recorder;
            var d = GestureTestFixtures.Definition(); var observations = GestureTestFixtures.Observations();
            Assert.IsTrue(recorder.Session.Begin(d, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), observations[0], 10, out var error), error);
            for (int i = 1; i < observations.Length; i++) recorder.Session.Tick(observations[i], "synthetic-source", 1, observations[i].TimestampSeconds);
            recorder.Stop();
            // The real window must retain this stopped, unsaved review through the editor transition.
            yield return new ExitPlayMode();
            var reopened = ScriptableObject.CreateInstance<GestureRecorderWindow>();
            Assert.IsNotNull(reopened.Review); Assert.IsTrue(reopened.Review.TryValidate(out _, out error), error);
            Assert.AreEqual(observations.Length, reopened.Review.CopyClip().Observations.Count);
            UnityEngine.Object.DestroyImmediate(reopened); reopened = ScriptableObject.CreateInstance<GestureRecorderWindow>();
            Assert.IsNotNull(reopened.Review); Assert.IsTrue(reopened.Review.TryValidate(out _, out error), error);
            reopened.Discard(); UnityEngine.Object.DestroyImmediate(reopened);
            foreach (var retained in Resources.FindObjectsOfTypeAll<GestureRecorderWindow>()) UnityEngine.Object.DestroyImmediate(retained);
        }
        [Test] public void WindowRetentionHookStopsActiveCaptureWithoutWritingAssets()
        {
            GestureReviewRetention.Clear(GestureRecorderWindow.ReviewKey);
            var owner = new GameObject("Window active-capture retention"); var window = ScriptableObject.CreateInstance<GestureRecorderWindow>();
            try
            {
                var recorder = owner.AddComponent<HandGestureRecorder>(); window.Recorder = recorder;
                var d = GestureTestFixtures.Definition(); var frames = GestureTestFixtures.Observations();
                Assert.IsTrue(recorder.Session.Begin(d, "synthetic-source", new GestureCalibration(Quaternion.identity, 1, 1), frames[0], 10, out _));
                foreach (var frame in frames) recorder.Session.Tick(frame, "synthetic-source", 1, frame.TimestampSeconds);
                window.Retain(); Assert.AreEqual(GestureRecordingState.ReadyForReview, recorder.Session.State);
                Assert.IsTrue(window.Review.TryValidate(out _, out var reason), reason);
                Assert.IsNotNull(GestureReviewRetention.Restore(GestureRecorderWindow.ReviewKey));
                Assert.AreEqual(0, AssetDatabase.FindAssets("t:GestureData", new[] { m_Folder }).Length);
                window.Discard();
            }
            finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
