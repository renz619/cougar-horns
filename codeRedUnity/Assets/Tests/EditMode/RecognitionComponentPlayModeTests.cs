using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace HandPoseRecognition.Tests
{
    public sealed class SyntheticRecognitionSource : MonoBehaviour, IHandFrameSource
    {
        public HandFrame Frame;
        public HandSide HandSide => HandSide.Right;
        public bool TryGetLatestFrame(out HandFrame frame) { frame = Frame; return frame != null; }
    }

    public class RecognitionComponentPlayModeTests
    {
        [UnityTest] public IEnumerator Components_RecordConfirmReleaseRecoverAndReturnToLegacy_InPlayMode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var owner = new GameObject("Synthetic component integration"); owner.SetActive(false);
            var labelOwner = new GameObject("Integration label", typeof(RectTransform));
            var template = ScriptableObject.CreateInstance<HandPoseData>(); var database = ScriptableObject.CreateInstance<PoseDatabase>();
            try
            {
                var source = owner.AddComponent<SyntheticRecognitionSource>();
                var adapter = owner.AddComponent<QuestHandAdapter>();
                var recorder = owner.AddComponent<HandPoseRecorder>(); recorder.Source = source;
                var controller = owner.AddComponent<RecognitionController>(); controller.Source = source;
                var display = owner.AddComponent<HandPoseDisplay>(); var label = labelOwner.AddComponent<TextMeshProUGUI>();
                typeof(HandPoseDisplay).GetField("m_Adapter", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(display, adapter);
                typeof(HandPoseDisplay).GetField("m_Label", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(display, label);
                var weights = HandPoseData.CreateDefaultWeights(); var canonical = RecognitionFixtures.Canonical();
                template.SetDefinition("synthetic.components", "Synthetic component fixture", HandSide.Right,
                    new[] { RecognitionFixtures.Sample(canonical, weights) }, weights, .01f);
                database.SetTemplates(new[] { template }); controller.Database = database;
                typeof(RecognitionController).GetField("m_Timing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller,
                    new RecognitionTimingSettings { MinimumEvaluationIntervalSeconds = 0, ConfirmationDwellSeconds = 0, EventCooldownSeconds = 0 });
                display.controller = controller;
                int events = 0; controller.PoseConfirmed += _ => events++;
                owner.SetActive(true); display.SelectBackend(RecognitionBackend.Template);
                Assert.IsTrue(recorder.Begin(out var reason), reason);
                source.Frame = RecognitionFixtures.Raw(Time.realtimeSinceStartupAsDouble, 1);
                yield return null;
                Assert.AreEqual(1, recorder.Session.AcceptedCount); Assert.AreEqual("synthetic.components", display.templatePoseId);
                Assert.AreEqual(1, events); Assert.That(label.text, Does.Contain("Synthetic component fixture"));
                source.Frame = RecognitionFixtures.Raw(Time.realtimeSinceStartupAsDouble, 2, tracked: false);
                yield return null;
                Assert.IsNull(display.templatePoseId); Assert.That(label.text, Does.StartWith("UNKNOWN")); Assert.AreEqual(1, events);
                source.Frame = RecognitionFixtures.Raw(Time.realtimeSinceStartupAsDouble, 3);
                yield return null;
                Assert.AreEqual("synthetic.components", display.templatePoseId); Assert.AreEqual(2, events);
                recorder.Stop(); Assert.GreaterOrEqual(recorder.Session.ExportReview().CopySamples().Length, 1);
                controller.enabled = false;
                yield return null;
                Assert.AreEqual(RecognitionBackend.Template, display.selectedBackend); Assert.IsNull(display.templatePoseId);
                controller.enabled = true; display.SelectBackend(RecognitionBackend.LegacyRules);
                yield return null;
                Assert.AreEqual(RecognitionBackend.LegacyRules, display.selectedBackend); Assert.IsNull(display.templatePoseId);
                Assert.That(label.text, Does.Contain("Waiting for Quest hand tracking"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(labelOwner);
                UnityEngine.Object.DestroyImmediate(template); UnityEngine.Object.DestroyImmediate(database);
            }
            yield return new ExitPlayMode();
        }
    }
}
