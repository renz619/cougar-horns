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
    public class PoseRecordingPersistenceTests
    {
        string m_Folder;
        [SetUp] public void Setup()
        {
            m_Folder = "Assets/HandPoses/RecognitionTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/HandPoses", System.IO.Path.GetFileName(m_Folder));
        }
        [TearDown] public void Cleanup()
        {
            if (!string.IsNullOrEmpty(m_Folder) && m_Folder.StartsWith("Assets/HandPoses/RecognitionTest_", StringComparison.Ordinal))
                AssetDatabase.DeleteAsset(m_Folder);
        }
        public static PoseRecordingReview Review(float shape = 0, string id = "recorded.test", int budget = 2)
        {
            var source = new object(); var session = new PoseRecordingSession();
            var settings = new PoseRecordingSettings { PoseId = id, DisplayName = "Recorded test shape", MaxAcceptedSamples = 2,
                MaxSamplesPerPose = budget, MinimumSampleIntervalSeconds = 0, Weights = RecognitionFixtures.Weights(1) };
            Assert.IsTrue(session.Begin(settings, source, 1, out var error), error);
            session.Tick(source, RecognitionSessionTests.Frame(1, 1, shape), 1);
            session.Tick(source, RecognitionSessionTests.Frame(1.125, 2, shape), 1.125);
            return session.ExportReview();
        }
        HandPoseData Create(PoseRecordingReview review, PoseDatabase catalog = null)
        {
            Assert.IsTrue(PoseRecordingPersistence.TrySave(review, new[] { 0 }, m_Folder + "/Pose.asset", null,
                RecordingSaveMode.Create, catalog, out var saved, out var error), error); return saved;
        }
        PoseDatabase Database()
        {
            var catalog = ScriptableObject.CreateInstance<PoseDatabase>();
            AssetDatabase.CreateAsset(catalog, m_Folder + "/Database.asset"); return catalog;
        }
        [Test] public void CreateSaveUnloadReloadValidatesTemplateAndCatalogProvenance()
        {
            var catalog = Database(); var saved = Create(Review(), catalog);
            string path = AssetDatabase.GetAssetPath(saved); AssetDatabase.SaveAssets();
            Resources.UnloadAsset(saved); Resources.UnloadAsset(catalog);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(m_Folder + "/Database.asset", ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<HandPoseData>(path);
            catalog = AssetDatabase.LoadAssetAtPath<PoseDatabase>(m_Folder + "/Database.asset");
            Assert.IsTrue(loaded.TryValidate(out var report), report.ToString()); Assert.AreEqual("recorded.test", loaded.PoseId);
            Assert.AreEqual(1, loaded.CopySamples()[0].SourceSequenceId); Assert.AreEqual(1, loaded.CopySamples()[0].SourceTimestampSeconds);
            Assert.IsTrue(catalog.TryBuildSnapshot(out var snapshot, out report), report.ToString()); Assert.AreEqual(1, snapshot.PoseCount);
        }
        [Test] public void UniqueCreateDoesNotOverwriteDestination()
        {
            var first = Create(Review()); var second = Create(Review());
            Assert.AreNotEqual(AssetDatabase.GetAssetPath(first), AssetDatabase.GetAssetPath(second)); Assert.AreEqual(1, first.SampleCount);
        }
        [Test] public void AppendPreservesIdentityAndRejectsCombinedBudgetBeforeWrites()
        {
            var review = Review(); var asset = Create(review);
            Assert.IsTrue(PoseRecordingPersistence.TrySave(review, new[] { 1 }, null, asset, RecordingSaveMode.Append, null, out _, out var error), error);
            Assert.AreEqual(2, asset.SampleCount); Assert.AreEqual("recorded.test", asset.PoseId);
            Assert.IsFalse(PoseRecordingPersistence.TrySave(review, new[] { 0 }, null, asset, RecordingSaveMode.Append, null, out _, out error));
            Assert.That(error, Does.Contain("budget")); Assert.AreEqual(2, asset.SampleCount);
        }
        [Test] public void ReplaceRequiresExactFrozenCompatibility()
        {
            var asset = Create(Review());
            Assert.IsFalse(PoseRecordingPersistence.TrySave(Review(id: "other"), new[] { 0 }, null, asset, RecordingSaveMode.Replace, null, out _, out var error));
            Assert.That(error, Does.Contain("ID")); Assert.AreEqual("recorded.test", asset.PoseId); Assert.AreEqual(1, asset.SampleCount);
        }
        [Test] public void DuplicateCatalogProposalRejectsWithoutCreatingAssetOrUpdatingCatalog()
        {
            var catalog = Database(); var first = Create(Review(), catalog); var prior = catalog.CopyTemplates();
            int files = AssetDatabase.FindAssets("t:HandPoseData", new[] { m_Folder }).Length;
            Assert.IsFalse(PoseRecordingPersistence.TrySave(Review(), new[] { 0 }, m_Folder + "/Duplicate.asset", null,
                RecordingSaveMode.Create, catalog, out _, out var error));
            Assert.That(error, Does.Contain("Duplicate ordinal ID")); CollectionAssert.AreEqual(prior, catalog.CopyTemplates());
            Assert.AreEqual(files, AssetDatabase.FindAssets("t:HandPoseData", new[] { m_Folder }).Length); Assert.AreEqual(1, first.SampleCount);
        }
        [Test] public void ExplicitReplaceAndRebuildLeavePublishedSnapshotUnchangedAndSupportUndo()
        {
            var catalog = Database(); var asset = Create(Review(), catalog); Assert.IsTrue(catalog.TryBuildSnapshot(out var old, out _));
            old.TryGetPose("recorded.test", out var oldPose); oldPose.Samples[0].TryGetJoint(HandJointId.Palm, out var oldJoint);
            Assert.IsTrue(PoseRecordingPersistence.TrySave(Review(1), new[] { 0, 1 }, null, asset,
                RecordingSaveMode.Replace, catalog, out _, out var error), error);
            Assert.AreEqual(1, old.SampleCount); Assert.IsTrue(catalog.TryBuildSnapshot(out var rebuilt, out _)); Assert.AreEqual(2, rebuilt.SampleCount);
            rebuilt.TryGetPose("recorded.test", out var newPose); newPose.Samples[0].TryGetJoint(HandJointId.Palm, out var newJoint);
            Assert.AreNotEqual(oldJoint.Position, newJoint.Position); oldPose.Samples[0].TryGetJoint(HandJointId.Palm, out var retained);
            Assert.AreEqual(oldJoint.Position, retained.Position);
            Undo.PerformUndo(); Assert.AreEqual(1, asset.SampleCount); Assert.AreEqual("recorded.test", asset.PoseId);
        }
        [TestCase("empty")] [TestCase("duplicate")] [TestCase("range")]
        public void InvalidSelectionDoesNotWrite(string kind)
        {
            int[] indices = kind == "empty" ? Array.Empty<int>() : kind == "duplicate" ? new[] { 0, 0 } : new[] { 2 };
            Assert.IsFalse(PoseRecordingPersistence.TrySave(Review(), indices, m_Folder + "/Invalid.asset", null,
                RecordingSaveMode.Create, null, out _, out var error)); Assert.IsNotEmpty(error);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<HandPoseData>(m_Folder + "/Invalid.asset"));
        }
        [Test] public void ZeroSampleSessionCannotSaveAndRawCanonicalMisuseIsRejected()
        {
            var session = new PoseRecordingSession(); var source = new object();
            Assert.IsTrue(session.Begin(new PoseRecordingSettings(), source, 1, out _));
            session.Tick(source, RecognitionFixtures.Canonical(), 1); Assert.AreEqual(1, session.RejectedCount);
            session.Stop(); Assert.IsFalse(PoseRecordingPersistence.TrySave(session.ExportReview(), new[] { 0 }, m_Folder + "/Zero.asset", null,
                RecordingSaveMode.Create, null, out _, out _));
        }
        [Test] public void SyntheticFixturesAreProtectedFromReviewedUpdate()
        {
            var synthetic = AssetDatabase.LoadAssetAtPath<PoseDatabase>("Assets/HandPoses/Synthetic/SyntheticDatabase.asset");
            var before = synthetic.CopyTemplates();
            Assert.IsFalse(PoseRecordingPersistence.TrySave(Review(), new[] { 0 }, m_Folder + "/SyntheticCatalogUpdate.asset", null,
                RecordingSaveMode.Create, synthetic, out _, out var error));
            Assert.That(error, Does.Contain("synthetic")); CollectionAssert.AreEqual(before, synthetic.CopyTemplates());
        }
        [Test] public void RetainedReviewSerializesOwnedDefinitionSamplesAndSelectionProvenance()
        {
            string key = "HandPoseRecognition.TestReview." + Guid.NewGuid().ToString("N");
            try
            {
                PoseReviewRetention.Store(key, Review()); var restored = PoseReviewRetention.Restore(key);
                Assert.AreEqual("recorded.test", restored.CopyDefinition().PoseId); Assert.AreEqual(2, restored.CopySamples().Length);
                var settings = restored.CopyDefinition(); settings.Weights[1] = 0; Assert.AreEqual(1, restored.CopyDefinition().Weights[1]);
                Create(restored);
            }
            finally { PoseReviewRetention.Clear(key); }
        }
        [UnityTest] public IEnumerator PlayModeExitAndDomainReloadRetainOwnedReviewWithoutSavingAssets()
        {
            const string key = "HandPoseRecognition.TestPlayModeReview.v1";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            PoseReviewRetention.Store(key, Review());
            yield return new ExitPlayMode();
            try
            {
                var restored = PoseReviewRetention.Restore(key);
                Assert.IsNotNull(restored); Assert.AreEqual(2, restored.CopySamples().Length);
                Assert.AreEqual("recorded.test", restored.CopyDefinition().PoseId);
                Assert.AreEqual(2, restored.CopySamples()[1].SourceSequenceId);
            }
            finally { PoseReviewRetention.Clear(key); }
        }
    }
}
