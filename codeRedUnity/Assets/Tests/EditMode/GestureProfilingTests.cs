using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace HandPoseRecognition.Tests
{
    public class GestureProfilingTests
    {
        const int Iterations = 128;
        static ProfilerRecorder Start() => ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 262144, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        static void Measure(string name, Action<int> action)
        {
            using (var recorder = Start())
            {
                long start = Stopwatch.GetTimestamp(); for (int i = 0; i < Iterations; i++) action(i);
                long elapsed = Stopwatch.GetTimestamp() - start; recorder.Stop();
                Assert.IsTrue(recorder.Valid); Assert.IsFalse(recorder.WrappedAround);
                TestContext.WriteLine($"GESTURE_PROFILE: {name}; iterations={Iterations}; allocation events={recorder.Count}; events/call={(double)recorder.Count / Iterations:F3}; us/call={(double)elapsed * 1000000 / Stopwatch.Frequency / Iterations:F3}");
            }
        }
        [UnityTest] public IEnumerator WarmedCaptureExtractionReplayRecorderAndDebugMeasuredSeparately()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); yield return new EnterPlayMode();
            RunMeasurements();
            yield return new ExitPlayMode();
        }
        static void RunMeasurements()
        {
            var definition = GestureTestFixtures.Definition(); var clip = GestureTestFixtures.Clip(definition); var asset = GestureTestFixtures.Asset(definition, clip);
            var owner = new GameObject("Test-owned debug formatting profile"); var catalogAsset = ScriptableObject.CreateInstance<GestureCatalog>();
            try
            {
                using (var control = Start()) { var allocation = new byte[1024]; GC.KeepAlive(allocation); control.Stop(); Assert.Greater(control.Count, 0, "Known allocation must be visible."); }
                var catalog = GestureTestFixtures.Catalog(asset); var matcher = new MotionGestureRecognizer();
                catalogAsset.SetEntries(new[] { asset }); var controller = owner.AddComponent<HandGestureController>();
                controller.Catalog = catalogAsset; controller.RebuildCatalog(); controller.Mode = GestureLessonMode.JZOnly;
                foreach (var observation in clip.Observations) controller.Session.Update(observation, new GestureCalibration(Quaternion.identity, 1, 1), catalog, observation.TimestampSeconds);
                var frames = new HandFrame[Iterations]; var observations = new GestureObservation[Iterations];
                for (int i = 0; i < Iterations; i++) { frames[i] = GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, 20 + i * .02, (ulong)i + 1); observations[i] = GestureTestFixtures.Observation(frames[i]); }
                var recordingDefinition = definition.Copy(); recordingDefinition.Settings.MaxDurationSeconds = 100; recordingDefinition.Settings.MaxFrames = Iterations + 1;
                var recording = new GestureRecordingSession(); var calibration = new GestureCalibration(Quaternion.identity, 1, 1);
                Assert.IsTrue(recording.Begin(recordingDefinition, "synthetic-source", calibration, observations[0], 20, out _));
                for (int i = 0; i < Iterations; i++)
                {
                    GestureTestFixtures.Observation(frames[i]); GestureFeatureExtractor.TryExtract(clip, out _, out _); matcher.Recognize(clip, catalog);
                    recording.Tick(observations[i], "synthetic-source", 1, observations[i].TimestampSeconds);
                    HandGestureDebugDisplay.Describe(controller);
                }
                recording.Clear(); Assert.IsTrue(recording.Begin(recordingDefinition, "synthetic-source", calibration, observations[0], 20, out _));
                TestContext.WriteLine($"GESTURE_PROFILE_SETUP: Unity {Application.unityVersion}; warmup={Iterations}; one right-side Z definition, one 43-frame clip, 64 comparison points, DTW band 12/run 2; source HandFrames preconstructed; events are counts, not bytes; Editor host, not Quest player.");
                Measure("Owned gesture observation capture", i => GestureTestFixtures.Observation(frames[i]));
                Measure("Feature extraction and preprocessing of complete clip", i => GestureFeatureExtractor.TryExtract(clip, out _, out _));
                Measure("Full complete-clip replay and DTW", i => matcher.Recognize(clip, catalog));
                Measure("Recorder incoming-frame admission", i => recording.Tick(observations[i], "synthetic-source", 1, observations[i].TimestampSeconds));
                Measure("Developer debug text formatting", i => HandGestureDebugDisplay.Describe(controller));
                Assert.AreEqual(Iterations, recording.AcceptedCount);
                TestContext.WriteLine($"GESTURE_WORKSPACE: reused rolling DTW arrays={matcher.WorkspaceBytes} bytes; maximum configured raw frames=256 (26 joints/frame); no retained arrays are pooled.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(catalogAsset); UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
