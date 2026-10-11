using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition.Tests
{
    public sealed class SyntheticGestureSource : MonoBehaviour, IHandFrameStream
    {
        public HandFrame Frame;
        public bool Available = true;
        public HandSide HandSide => HandSide.Right;
        public event Action<HandFrame> FramePublished;
        public bool TryGetLatestFrame(out HandFrame frame) { frame = Frame; return Available && frame != null; }
        public void Publish(HandFrame frame) { Frame = frame; FramePublished?.Invoke(frame); }
    }
    public class GestureComponentTests
    {
        static void Invoke(object component, string method) => component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        [Test] public void AdapterStreamPublishesEveryDynamicAndLossButNeverBeforeRender()
        {
            var subsystem = FrameTestProvider.CreateSubsystem(); var owner = new GameObject("Gesture adapter stream test");
            try
            {
                subsystem.Start(); var adapter = owner.AddComponent<QuestHandAdapter>(); HandFrameAdapterTests.Invoke(adapter, "Update");
                int events = 0; HandFrame last = null; adapter.FramePublished += frame => { events++; last = frame; };
                subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic); subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                Assert.AreEqual(2, events); Assert.IsTrue(last.IsTracked);
                subsystem.TryUpdateHands(XRHandSubsystem.UpdateType.BeforeRender); adapter.TryGetLatestFrame(out _); Assert.AreEqual(2, events);
                adapter.enabled = false; HandFrameAdapterTests.Invoke(adapter, "OnDisable"); Assert.IsFalse(last.IsTracked); Assert.GreaterOrEqual(events, 3);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); subsystem.Stop(); subsystem.Destroy(); }
        }
        [Test] public void ReferenceProviderFreezesNumericalAxesButDetectsRigChanges()
        {
            var rig = new GameObject("Motion reference rig"); var head = new GameObject("Motion reference head");
            try
            {
                head.transform.SetParent(rig.transform, false); rig.transform.rotation = Quaternion.Euler(10, 20, 0);
                var provider = rig.AddComponent<GestureReferenceProvider>(); provider.TrackingSpace = rig.transform; provider.Head = head.transform;
                Assert.IsTrue(provider.TryGetCalibration(out var calibration, out var reason), reason);
                Assert.IsTrue(calibration.TryGetBasis(out var before, out _)); ulong epoch = provider.CoordinateEpoch;
                head.transform.localRotation = Quaternion.Euler(0, 60, 0);
                Assert.IsTrue(provider.TryGetCalibration(out var next, out _)); Assert.AreEqual(epoch, provider.CoordinateEpoch);
                Assert.IsTrue(calibration.TryGetBasis(out var unchanged, out _)); Assert.AreEqual(before, unchanged, "Frozen numerical calibration must not follow the head.");
                Assert.IsTrue(next.TryGetBasis(out var after, out _)); Assert.AreNotEqual(before, after);
                rig.transform.position += Vector3.right; Assert.Greater(provider.CoordinateEpoch, epoch);
                rig.transform.localScale = new Vector3(1, 2, 1); Assert.IsFalse(provider.TryGetCalibration(out _, out _));
            }
            finally { UnityEngine.Object.DestroyImmediate(head); UnityEngine.Object.DestroyImmediate(rig); }
        }
        [Test] public void ComponentsHonorFalseSourceReturnEvenWithNonnullFrame()
        {
            var owner = new GameObject("Unavailable gesture source");
            try
            {
                var source = owner.AddComponent<SyntheticGestureSource>(); source.Frame = GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, Time.realtimeSinceStartupAsDouble, 1); source.Available = false;
                var reference = owner.AddComponent<GestureReferenceProvider>(); reference.TrackingSpace = owner.transform; reference.Head = owner.transform;
                var recorder = owner.AddComponent<HandGestureRecorder>(); recorder.Source = source; recorder.ReferenceProvider = reference; Invoke(recorder, "OnEnable");
                Assert.IsFalse(recorder.Begin(out _)); Assert.AreEqual(0, recorder.Session.AcceptedCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        [UnityTest] public IEnumerator MotionComponentsCaptureCompleteLiveSequenceAndClearOnDisable()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); yield return new EnterPlayMode();
            var owner = new GameObject("Synthetic motion component integration"); owner.SetActive(false);
            var d = GestureTestFixtures.Definition(); d.Settings.MaxGapSeconds = .3; d.Settings.MaxDurationSeconds = 4;
            var asset = GestureTestFixtures.Asset(d, GestureTestFixtures.Clip(d)); var catalog = ScriptableObject.CreateInstance<GestureCatalog>(); catalog.SetEntries(new[] { asset });
            try
            {
                var source = owner.AddComponent<SyntheticGestureSource>(); var reference = owner.AddComponent<GestureReferenceProvider>(); reference.TrackingSpace = owner.transform; reference.Head = owner.transform;
                var recorder = owner.AddComponent<HandGestureRecorder>(); recorder.Source = source; recorder.ReferenceProvider = reference; recorder.Definition = d;
                var controller = owner.AddComponent<HandGestureController>(); controller.Source = source; controller.ReferenceProvider = reference; controller.Catalog = catalog; controller.Mode = GestureLessonMode.JZOnly;
                int events = 0; controller.GestureConfirmed += _ => events++; owner.SetActive(true); yield return null;
                var examples = GestureTestFixtures.Observations(); ulong sequence = 1;
                source.Publish(GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, Time.realtimeSinceStartupAsDouble, sequence++));
                Assert.IsTrue(recorder.Begin(out var reason), reason);
                for (int i = 1; i < examples.Length; i++)
                {
                    yield return new WaitForSecondsRealtime(.025f);
                    examples[i].Right.TryGetJoint(HandJointId.IndexTip, out var tip); examples[0].Right.TryGetJoint(HandJointId.IndexTip, out var initial);
                    source.Publish(GestureTestFixtures.Frame(GestureMotionKind.Z, (tip.Position - initial.Position) / .06f, Time.realtimeSinceStartupAsDouble, sequence++));
                }
                Assert.AreEqual(1, events, controller.Session.Reason); recorder.Stop(); Assert.IsTrue(recorder.Session.ExportReview().TryValidate(out _, out reason), reason);
                Assert.Greater(recorder.Session.AcceptedCount, 30, "Motion capture must not inherit a 10Hz evaluation interval.");
                controller.enabled = false; Assert.IsNull(controller.Session.ConfirmedGestureId);
                Assert.That(HandGestureDebugDisplay.Describe(controller), Does.Contain("UNAVAILABLE"));
                controller.enabled = true; Assert.IsNull(controller.Session.ConfirmedGestureId);
                source.Available = false; yield return null; Assert.IsNull(controller.Session.ConfirmedGestureId);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(catalog); }
            yield return new ExitPlayMode();
        }
        [TestCase(false)] [TestCase(true)] public void DisabledOrDestroyedRequiredSourceInvalidatesRecordingImmediately(bool destroy)
        {
            var owner = new GameObject("Required motion source lifecycle");
            try
            {
                var source = owner.AddComponent<SyntheticGestureSource>();
                var reference = owner.AddComponent<GestureReferenceProvider>(); reference.TrackingSpace = owner.transform; reference.Head = owner.transform;
                var recorder = owner.AddComponent<HandGestureRecorder>(); recorder.Source = source; recorder.ReferenceProvider = reference; Invoke(recorder, "OnEnable");
                source.Frame = GestureTestFixtures.Frame(GestureMotionKind.Z, Vector3.zero, Time.realtimeSinceStartupAsDouble, 1);
                Assert.IsTrue(recorder.Begin(out var reason), reason);
                if (destroy) UnityEngine.Object.DestroyImmediate(source); else source.enabled = false;
                Invoke(recorder, "Update");
                Assert.AreEqual(GestureRecordingState.ReadyForReview, recorder.Session.State);
                Assert.That(recorder.Session.LastRejectionReason, Does.Contain("disabled or destroyed"));
                Assert.IsFalse(recorder.Session.ExportReview().TryValidate(out _, out _));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        [UnityTest] public IEnumerator ZFeedbackFlashesThenStaysThroughLossResetAndDisable()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); yield return new EnterPlayMode();
            var owner = new GameObject("Session Z feedback integration");
            var labelOwner = new GameObject("Separate confirmation label", typeof(RectTransform));
            var label = labelOwner.AddComponent<TMPro.TextMeshProUGUI>();
            var controller = owner.AddComponent<HandGestureController>();
            var feedback = owner.AddComponent<HandGestureDetectionDisplay>(); feedback.Controller = controller; feedback.Label = label;
            var jDefinition = GestureTestFixtures.Definition(GestureMotionKind.J, "asl.letter.j");
            var zDefinition = GestureTestFixtures.Definition(GestureMotionKind.Z, "asl.letter.z");
            var jAsset = GestureTestFixtures.Asset(jDefinition, GestureTestFixtures.Clip(jDefinition));
            var zAsset = GestureTestFixtures.Asset(zDefinition, GestureTestFixtures.Clip(zDefinition));
            try
            {
                Assert.IsFalse(label.enabled); Assert.IsFalse(feedback.HasDetected);
                var calibration = new GestureCalibration(Quaternion.identity, 1, 1);
                var jCatalog = GestureTestFixtures.Catalog(jAsset);
                foreach (var frame in GestureTestFixtures.Observations(GestureMotionKind.J))
                    controller.Session.Update(frame, calibration, jCatalog, frame.TimestampSeconds);
                Assert.AreEqual("asl.letter.j", controller.Session.ConfirmedGestureId);
                Assert.IsFalse(feedback.HasDetected, "A J confirmation must not latch the Z text.");
                controller.Session.Reset(); var zCatalog = GestureTestFixtures.Catalog(zAsset);
                foreach (var frame in GestureTestFixtures.Observations()) controller.Session.Update(frame, calibration, zCatalog, frame.TimestampSeconds);
                Assert.IsTrue(feedback.HasDetected); Assert.IsTrue(label.enabled);
                Assert.AreEqual("Z DETECTED", label.text); Assert.AreEqual(Color.red, label.color);
                double deadline = Time.unscaledTimeAsDouble + 1;
                while (label.color.a != 0 && Time.unscaledTimeAsDouble < deadline) yield return null;
                Assert.AreEqual(0, label.color.a, "Confirmed text must visibly blink during the initial flash.");
                yield return new WaitForSecondsRealtime(1.6f);
                Assert.AreEqual(Color.red, label.color); Assert.IsTrue(label.enabled);
                controller.Session.Update(null, calibration, zCatalog, 11.01); controller.Session.Reset();
                controller.enabled = false; feedback.enabled = false;
                Assert.IsTrue(feedback.HasDetected); Assert.IsTrue(label.enabled); Assert.AreEqual(Color.red, label.color);
                feedback.enabled = true; yield return null;
                Assert.IsTrue(feedback.HasDetected); Assert.IsTrue(label.enabled); Assert.AreEqual(Color.red, label.color);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(labelOwner);
                UnityEngine.Object.DestroyImmediate(jAsset); UnityEngine.Object.DestroyImmediate(zAsset);
            }
            yield return new ExitPlayMode();
        }
    }
}
