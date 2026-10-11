using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using HandPoseRecognition.Editor;

namespace HandPoseRecognition.Tests
{
    public class PoseDatabasePersistenceTests
    {
        string m_Folder;
        [SetUp]
        public void SetUp()
        {
            m_Folder = "Assets/__HandPosePersistenceTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", m_Folder.Substring("Assets/".Length));
        }
        [TearDown]
        public void TearDown()
        {
            // This unique test-owned folder is the only cleanup target.
            AssetDatabase.DeleteAsset(m_Folder);
        }

        [Test]
        public void SaveUnloadReload_PreservesSamplesAndBuildsEquivalentSnapshot()
        {
            Assert.IsTrue(PoseDatabaseAuthoring.TryCreateSyntheticSample(out var sample, out var reason), reason);
            var template = ScriptableObject.CreateInstance<HandPoseData>();
            template.SetDefinition("synthetic.persistence.right", "SYNTHETIC persistence test", HandSide.Right,
                new[] { sample, PoseDatabaseTests.Sample() }, HandPoseData.CreateDefaultWeights(), .1f);
            var database = ScriptableObject.CreateInstance<PoseDatabase>();
            database.SetTemplates(new[] { template });
            string templatePath = m_Folder + "/Template.asset";
            string dbPath = m_Folder + "/Database.asset";
            AssetDatabase.CreateAsset(template, templatePath);
            AssetDatabase.CreateAsset(database, dbPath);
            AssetDatabase.SaveAssets();
            Assert.IsTrue(database.TryBuildSnapshot(out var before, out var report), report.ToString());
            // Unload both assets, rather than merely reading Unity's existing cached instances.
            Resources.UnloadAsset(database);
            Resources.UnloadAsset(template);
            AssetDatabase.ImportAsset(templatePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(dbPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<PoseDatabase>(dbPath);
            Assert.IsTrue(reloaded.TryBuildSnapshot(out var after, out report), report.ToString());
            Assert.AreEqual(1, after.PoseCount);
            Assert.AreEqual(2, after.SampleCount);
            var expected = before.Templates[0];
            var actual = after.Templates[0];
            Assert.AreEqual(expected.PoseId, actual.PoseId);
            Assert.AreEqual(expected.DisplayName, actual.DisplayName);
            Assert.AreEqual(HandSide.Right, actual.HandSide);
            Assert.AreEqual(1, actual.JointLayoutVersion);
            Assert.AreEqual(1, actual.NormalizationVersion);
            for (int s = 0; s < 2; s++)
            {
                Assert.AreEqual(expected.Samples[s].SourceTimestampSeconds, actual.Samples[s].SourceTimestampSeconds);
                Assert.AreEqual(expected.Samples[s].SourceSequenceId, actual.Samples[s].SourceSequenceId);
                for (int i = 0; i < 26; i++)
                {
                    expected.Samples[s].TryGetJoint((HandJointId)i, out var a);
                    actual.Samples[s].TryGetJoint((HandJointId)i, out var b);
                    Assert.AreEqual(a.Position, b.Position);
                    Assert.AreEqual(a.Rotation, b.Rotation);
                    Assert.AreEqual(a.HasPosition, b.HasPosition);
                    Assert.AreEqual(a.HasRotation, b.HasRotation);
                }
            }
        }

        [Test]
        public void CreateMenus_AreAvailableForTemplateAndDatabase()
        {
            var template = (CreateAssetMenuAttribute)Attribute.GetCustomAttribute(typeof(HandPoseData), typeof(CreateAssetMenuAttribute));
            var db = (CreateAssetMenuAttribute)Attribute.GetCustomAttribute(typeof(PoseDatabase), typeof(CreateAssetMenuAttribute));
            Assert.AreEqual("Hand Pose/Template", template.menuName);
            Assert.AreEqual("Hand Pose/Database", db.menuName);
        }
    }
}
