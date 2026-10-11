using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class TemplateDisplayIntegrationTests
    {
        GameObject m_Owner, m_LabelOwner;
        HandPoseDisplay m_Display;
        RecognitionController m_Controller;
        TMP_Text m_Label;
        PoseDatabase m_Database;
        HandPoseData m_Template;
        PoseDatabaseSnapshot m_Snapshot;
        object m_Source;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        [SetUp] public void Setup()
        {
            m_Owner = new GameObject("Template display test"); m_Owner.SetActive(false);
            m_LabelOwner = new GameObject("Template label", typeof(RectTransform));
            m_Label = m_LabelOwner.AddComponent<TextMeshProUGUI>();
            var adapter = m_Owner.AddComponent<QuestHandAdapter>();
            m_Display = m_Owner.AddComponent<HandPoseDisplay>(); m_Controller = m_Owner.AddComponent<RecognitionController>();
            Set(m_Display, "m_Adapter", adapter); Set(m_Display, "m_Label", m_Label);
            m_Template = ScriptableObject.CreateInstance<HandPoseData>(); m_Database = ScriptableObject.CreateInstance<PoseDatabase>();
            var q = RecognitionFixtures.Canonical(); var weights = RecognitionFixtures.Weights(1);
            m_Template.SetDefinition("test.id", "Recorded display shape", HandSide.Right, new[] { RecognitionFixtures.Sample(q, weights) }, weights, .01f);
            m_Database.SetTemplates(new[] { m_Template }); m_Controller.Database = m_Database;
            Assert.IsTrue(m_Database.TryBuildSnapshot(out m_Snapshot, out _)); m_Source = new object();
            m_Owner.SetActive(true);
            Invoke(adapter, "Update");
            Invoke(m_Controller, "OnEnable");
            Invoke(m_Display, "OnEnable");
        }
        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(m_Owner); UnityEngine.Object.DestroyImmediate(m_LabelOwner);
            UnityEngine.Object.DestroyImmediate(m_Database); UnityEngine.Object.DestroyImmediate(m_Template);
        }
        void Template(bool confirm)
        {
            m_Display.controller = m_Controller; m_Display.SelectBackend(RecognitionBackend.Template);
            var timing = new RecognitionTimingSettings { ConfirmationDwellSeconds = confirm ? 0 : .2 };
            m_Controller.Session.Update(m_Source, RecognitionFixtures.Raw(), m_Snapshot, RecognitionBackend.Template, timing, 1);
            Invoke(m_Display, "Update");
        }
        [Test] public void DefaultLegacyPathNeedsNoControllerAndKeepsOldHints()
        {
            Assert.AreEqual(RecognitionBackend.LegacyRules, m_Display.selectedBackend); Assert.IsNull(m_Display.controller);
            Assert.That(m_Label.text, Does.Contain("Waiting for Quest hand tracking")); Assert.IsNull(m_Display.templatePoseId);
        }
        [Test] public void TemplateConfirmedDisplaysNameIdAndScoreAndLegacyPoseStaysUnknown()
        {
            Template(true); Assert.That(m_Label.text, Does.Contain("Recorded display shape (test.id)"));
            Assert.That(m_Label.text, Does.Contain("Confirmed")); Assert.That(m_Label.text, Does.Contain("error 0.0000"));
            Assert.AreEqual(HandPose.Unknown, m_Display.currentPose); Assert.AreEqual("test.id", m_Display.templatePoseId);
            Assert.That(m_Label.text, Does.Contain("Thumb Unknown"));
        }
        [Test] public void PendingShowsUnknownHoldingAndPreservesMatchedObservationScore()
        {
            Template(false); Assert.That(m_Label.text, Does.StartWith("UNKNOWN")); Assert.That(m_Label.text, Does.Contain("Holding"));
            Assert.That(m_Label.text, Does.Contain("Observation Matched")); Assert.IsTrue(m_Display.templateResult.IsMatch); Assert.IsNull(m_Display.templatePoseId);
        }
        [Test] public void DisabledControllerClearsLabelWithoutLegacyFallback()
        {
            Template(true); m_Controller.enabled = false; Invoke(m_Controller, "OnDisable"); Invoke(m_Display, "Update");
            Assert.That(m_Label.text, Does.StartWith("UNKNOWN")); Assert.That(m_Label.text, Does.Contain("controller is missing or disabled"));
            Assert.That(m_Label.text, Does.Contain("error n/a")); Assert.IsNull(m_Display.templatePoseId); Assert.AreEqual(RecognitionBackend.Template, m_Display.selectedBackend);
            m_Controller.enabled = true; Invoke(m_Controller, "OnEnable"); Invoke(m_Display, "Update"); Assert.IsNull(m_Display.templatePoseId);
        }
        [Test] public void DestroyedAndDisconnectedControllerKeepsTemplateUntilExplicitModeReset()
        {
            Template(true); UnityEngine.Object.DestroyImmediate(m_Controller); Invoke(m_Display, "Update");
            Assert.AreEqual(RecognitionBackend.Template, m_Display.selectedBackend); Assert.That(m_Label.text, Does.Contain("Unavailable"));
            m_Display.controller = null; Invoke(m_Display, "Update"); Assert.AreEqual(RecognitionBackend.Template, m_Display.selectedBackend);
            m_Display.SelectBackend(RecognitionBackend.LegacyRules); Invoke(m_Display, "Update");
            Assert.That(m_Label.text, Does.Contain("Waiting for Quest hand tracking"));
        }
        [Test] public void ExplicitTemplateWithoutControllerIsUnavailableThenLegacyCanBeSelected()
        {
            m_Display.SelectBackend(RecognitionBackend.Template); Invoke(m_Display, "Update");
            Assert.That(m_Label.text, Does.Contain("Unavailable")); Assert.IsFalse(m_Display.templateResult.HasScore);
            m_Display.SelectBackend(RecognitionBackend.LegacyRules); Invoke(m_Display, "Update");
            Assert.That(m_Label.text, Does.Contain("Waiting for Quest hand tracking"));
        }
        [Test] public void CatalogStartupFailureAndFailedReloadRetentionAreExplicit()
        {
            Template(true); m_Database.SetTemplates(new[] { m_Template, m_Template });
            Assert.IsFalse(m_Controller.RebuildCatalog(true)); Assert.That(m_Controller.CatalogReport, Does.Contain("explicitly retaining"));
            Invoke(m_Display, "Update"); Assert.That(m_Label.text, Does.Contain("Reload failed")); Assert.AreEqual("test.id", m_Display.templatePoseId);
            Assert.IsFalse(m_Controller.RebuildCatalog()); Invoke(m_Display, "Update"); Assert.IsNull(m_Display.templatePoseId);
            Assert.That(m_Label.text, Does.Contain("Duplicate ordinal ID"));
        }
        [Test] public void DatabaseAndSourcePropertyChangesClearCurrentState()
        {
            Template(true); m_Controller.Database = null; Assert.IsNull(m_Controller.Session.ConfirmedPoseId);
            Assert.IsFalse(m_Controller.RebuildCatalog()); Assert.That(m_Controller.CatalogReport, Does.Contain("No database"));
            Template(true); m_Controller.Source = m_Owner.GetComponent<QuestHandAdapter>(); Assert.IsNull(m_Controller.Session.ConfirmedPoseId);
        }
        [Test] public void DisabledControllerUnsubscribesAndReenableDoesNotDuplicateForwarding()
        {
            int events = 0; m_Controller.PoseConfirmed += _ => events++;
            Template(true); Assert.AreEqual(1, events); m_Controller.enabled = false; Invoke(m_Controller, "OnDisable");
            Template(true); Assert.AreEqual(1, events); m_Controller.enabled = true; Invoke(m_Controller, "OnEnable");
            Template(true); Assert.AreEqual(2, events);
        }
        [Test] public void NoScoreFormattingDoesNotClaimZeroError()
        {
            var text = HandPoseDisplay.DescribeTemplate(default, RecognitionControllerStatus.Unavailable, "No source");
            Assert.That(text, Does.Contain("error n/a")); Assert.That(text, Does.Not.Contain("0.0000"));
        }
    }
}
