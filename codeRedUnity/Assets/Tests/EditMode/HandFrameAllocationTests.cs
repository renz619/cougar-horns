using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition.Tests
{
    public class HandFrameAllocationTests
    {
        const int Warmup = 128;
        const int Iterations = 512;

        static void Update(XRHandSubsystem subsystem, QuestHandAdapter adapter, bool normalize)
        {
            subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            if (normalize && adapter.TryGetLatestFrame(out var raw))
            {
                if (!HandPoseNormalizer.TryNormalizeFrame(raw, out _, out var reason))
                    throw new InvalidOperationException(reason);
            }
        }

        static double Measure(XRHandSubsystem subsystem, QuestHandAdapter adapter, bool normalize)
        {
            for (int i = 0; i < Warmup; i++) Update(subsystem, adapter, normalize);
            using (var recorder = StartRecorder())
            {
                for (int i = 0; i < Iterations; i++) Update(subsystem, adapter, normalize);
                recorder.Stop();
                return (double)AllocationCount(recorder) / Iterations;
            }
        }

        static ProfilerRecorder StartRecorder() => ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192,
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

        static long AllocationCount(ProfilerRecorder recorder)
        {
            Assert.IsTrue(recorder.Valid, "GC.Alloc must be supported; no invented allocation results.");
            Assert.IsFalse(recorder.WrappedAround, "Recorder capacity must cover every allocation.");
            // GC.Alloc samples record events; Value is marker time, not allocation byte size.
            return recorder.Count;
        }

        [UnityTest]
        public IEnumerator PlayMode_WarmedSyntheticDynamicUpdates_ReportManagedAllocations()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var subsystem = FrameTestProvider.CreateSubsystem();
            var owner = new GameObject("Synthetic allocation probe");
            try
            {
                using (var control = StartRecorder())
                {
                    var knownAllocation = new byte[1024];
                    GC.KeepAlive(knownAllocation);
                    control.Stop();
                    Assert.GreaterOrEqual(AllocationCount(control), 1, "Positive allocation control must be visible before trusting the recorder.");
                }
                subsystem.Start();
                double xrOnly = Measure(subsystem, null, false);
                var adapter = owner.AddComponent<QuestHandAdapter>();
                HandFrameAdapterTests.Invoke(adapter, "Update");
                double withCapture = Measure(subsystem, adapter, false);
                double withNormalization = Measure(subsystem, adapter, true);
                Assert.Greater(withCapture, xrOnly, "Owned capture snapshots must allocate.");
                Assert.Greater(withNormalization, withCapture, "Canonical snapshots must allocate.");
                adapter.TryGetLatestFrame(out var retained);
                for (int i = 0; i < Warmup; i++) adapter.TryGetLatestFrame(out _);
                long readAllocations;
                using (var reads = StartRecorder())
                {
                    for (int i = 0; i < Iterations; i++) adapter.TryGetLatestFrame(out _);
                    reads.Stop();
                    readAllocations = AllocationCount(reads);
                }
                adapter.TryGetLatestFrame(out var same);
                Assert.AreSame(retained, same);
                Assert.AreEqual(0, readAllocations, "Repeated frame reads must not allocate duplicate snapshots.");
                TestContext.WriteLine($"PLAY_MODE_ALLOCATION: Unity {Application.unityVersion}; synthetic XR provider reports 2 hands, 1 selected-hand adapter; warmup={Warmup}, measured Dynamic updates={Iterations}; GC.Alloc events/update on current thread: XR only={xrOnly:F0}, XR+capture={withCapture:F0}, capture increment={withCapture - xrOnly:F0}, XR+capture+normalize={withNormalization:F0}, normalize increment={withNormalization - withCapture:F0}; repeated reads allocations={readAllocations}. No allocation-byte or headset/Quest player timing measurement.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                subsystem.Stop();
                subsystem.Destroy();
            }
            yield return new ExitPlayMode();
        }
    }
}
