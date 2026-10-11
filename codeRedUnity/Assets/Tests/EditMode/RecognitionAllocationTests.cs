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
    public class RecognitionAllocationTests
    {
        const int Iterations = 512;
        static ProfilerRecorder Start() => ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 262144,
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        static long Count(ProfilerRecorder recorder)
        {
            Assert.IsTrue(recorder.Valid); Assert.IsFalse(recorder.WrappedAround); return recorder.Count;
        }
        static void Measure(string operation, Action<int> action, bool expectZero = false)
        {
            long events, elapsed;
            using (var recorder = Start())
            {
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < Iterations; i++) action(i);
                elapsed = Stopwatch.GetTimestamp() - start;
                recorder.Stop(); events = Count(recorder);
            }
            if (expectZero) Assert.AreEqual(0, events, operation);
            TestContext.WriteLine($"RECOGNITION_PROFILE: {operation}; measured={Iterations}; GC.Alloc events={events}, events/call={(double)events / Iterations:F4}; elapsed us/call={(double)elapsed * 1000000 / Stopwatch.Frequency / Iterations:F3}. Allocation events, not bytes; synthetic Editor host timing, not Quest player CPU.");
        }
        [UnityTest] public IEnumerator PlayMode_WarmedMatcherNormalizerControllerRecorder_MeasuredSeparately()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var template = ScriptableObject.CreateInstance<HandPoseData>(); var database = ScriptableObject.CreateInstance<PoseDatabase>();
            try
            {
                using (var control = Start())
                {
                    var allocation = new byte[1024]; GC.KeepAlive(allocation); control.Stop();
                    Assert.GreaterOrEqual(Count(control), 1, "Visible positive control required.");
                }
                var raw = new HandFrame[Iterations]; var canonical = new HandFrame[Iterations];
                for (int i = 0; i < Iterations; i++)
                {
                    raw[i] = RecognitionFixtures.Raw(2 + i * .125, (ulong)i);
                    Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(raw[i], out canonical[i], out _));
                }
                var weights = HandPoseData.CreateDefaultWeights();
                template.SetDefinition("synthetic.profile", "Synthetic profiling fixture", HandSide.Right,
                    new[] { RecognitionFixtures.Sample(canonical[0], weights) }, weights, .1f);
                database.SetTemplates(new[] { template }); Assert.IsTrue(database.TryBuildSnapshot(out var snapshot, out _));
                var source = new object(); var session = new RecognitionSession();
                var timing = new RecognitionTimingSettings { MinimumEvaluationIntervalSeconds = .125, ConfirmationDwellSeconds = 0 };
                var recording = new PoseRecordingSession();
                var settings = new PoseRecordingSettings { MaxSamplesPerPose = Iterations, MaxAcceptedSamples = Iterations,
                    MaxAttempts = Iterations, MaxDurationSeconds = 1000, MinimumSampleIntervalSeconds = 0 };
                Assert.IsTrue(recording.Begin(settings, source, 2, out _));
                for (int i = 0; i < Iterations; i++)
                {
                    ShapeRecognizer.Recognize(canonical[i], snapshot, raw[i].TimestampSeconds, .25);
                    HandPoseNormalizer.TryNormalizeFrame(raw[i], out _, out _);
                    session.Update(source, raw[i], snapshot, RecognitionBackend.Template, timing, raw[i].TimestampSeconds);
                    recording.Tick(source, raw[i], raw[i].TimestampSeconds);
                }
                session.Reset(); recording.Clear(); Assert.IsTrue(recording.Begin(settings, source, 2, out _));
                TestContext.WriteLine($"RECOGNITION_PROFILE_SETUP: Unity {Application.unityVersion}; warmup={Iterations}; one source, one right-side candidate, one sample, 26 positive weights; preconstructed acquisitions exclude source capture; recorder budget={Iterations}, sufficient capacity warmed; positive control visible.");
                Measure("Pure matcher", i => ShapeRecognizer.Recognize(canonical[i], snapshot, raw[i].TimestampSeconds, .25), true);
                Measure("Normalization only", i => HandPoseNormalizer.TryNormalizeFrame(raw[i], out _, out _));
                Measure("Controller helper including normalization/matching", i => session.Update(source, raw[i], snapshot, RecognitionBackend.Template, timing, raw[i].TimestampSeconds));
                Measure("Recorder helper including normalization/owned sample conversion", i => recording.Tick(source, raw[i], raw[i].TimestampSeconds));
                Assert.AreEqual(Iterations, recording.AcceptedCount);
                Measure("Controller repeated fresh acquisition", i => session.Update(source, raw[Iterations - 1], snapshot,
                    RecognitionBackend.Template, timing, raw[Iterations - 1].TimestampSeconds), true);
            }
            finally { UnityEngine.Object.DestroyImmediate(database); UnityEngine.Object.DestroyImmediate(template); }
            yield return new ExitPlayMode();
        }
    }
}
