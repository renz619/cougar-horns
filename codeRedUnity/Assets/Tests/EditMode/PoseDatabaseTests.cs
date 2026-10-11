using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HandPoseRecognition.Tests
{
    public class PoseDatabaseTests
    {
        readonly List<Object> m_Assets = new List<Object>();
        public static void SetField(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            m_Assets.Add(asset);
            return asset;
        }
        [TearDown] public void TearDown() { foreach (var asset in m_Assets) Object.DestroyImmediate(asset); m_Assets.Clear(); }

        public static PoseSample Sample(HandSide side = HandSide.Right, JointPose[] joints = null, float[] weights = null)
        {
            Assert.IsTrue(HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints, side), out var frame, out var reason), reason);
            Assert.IsTrue(PoseSample.TryCreate(frame, weights ?? HandPoseData.CreateDefaultWeights(), out var sample, out var report), report.ToString());
            return sample;
        }
        HandPoseData Template(string id = "shape.synthetic.right", HandSide side = HandSide.Right)
        {
            var asset = Create<HandPoseData>();
            asset.SetDefinition(id, "Synthetic fixture", side, new[] { Sample(side) }, HandPoseData.CreateDefaultWeights(), .1f);
            return asset;
        }

        [Test]
        public void DefaultsAndSharedConversion_AreExplicit()
        {
            var template = Create<HandPoseData>();
            Assert.AreEqual(32, template.MaxSamplesPerPose);
            foreach (float weight in template.CopyJointWeights()) Assert.AreEqual(1f, weight);
            Assert.IsFalse(template.TryValidate(out _));
            Assert.IsFalse(PoseSample.TryCreate(HandFrameFixtures.Raw(), HandPoseData.CreateDefaultWeights(), out var sample, out var report));
            Assert.IsNull(sample);
            StringAssert.Contains("canonical", report.ToString());
        }

        [Test]
        public void MultiSampleAndInputCopies_DisplayNameDoesNotControlIdentity()
        {
            var sample = Sample();
            var input = new[] { sample, Sample() };
            var weights = HandPoseData.CreateDefaultWeights();
            var asset = Create<HandPoseData>();
            asset.SetDefinition("shape.test", "First name", HandSide.Right, input, weights, 0);
            input[0] = null;
            weights[0] = -1;
            SetField(sample, "m_HandSide", HandSide.Unknown);
            Assert.IsTrue(asset.TryValidate(out var report), report.ToString());
            Assert.AreEqual(2, asset.SampleCount);
            SetField(asset, "m_DisplayName", "Renamed");
            Assert.AreEqual("shape.test", asset.PoseId);
            Assert.IsTrue(asset.TryValidate(out report), report.ToString());
        }

        [Test]
        public void MissingPositiveWeightJoint_Fails_ZeroWeightAllowsIt()
        {
            var joints = HandFrameFixtures.Joints();
            joints[10].HasPosition = false;
            HandPoseNormalizer.TryNormalizeFrame(HandFrameFixtures.Raw(joints), out var canonical, out _);
            var weights = HandPoseData.CreateDefaultWeights();
            Assert.IsFalse(PoseSample.TryCreate(canonical, weights, out _, out var report));
            StringAssert.Contains("IndexTip", report.ToString());
            weights[10] = 0;
            Assert.IsTrue(PoseSample.TryCreate(canonical, weights, out var sample, out report), report.ToString());
            var template = Create<HandPoseData>();
            template.SetDefinition("shape.partial", "Partial synthetic", HandSide.Right, new[] { sample }, weights, .1f);
            Assert.IsTrue(template.TryValidate(out report), report.ToString());
            weights[10] = 1;
            template.SetDefinition("shape.partial", "Partial synthetic", HandSide.Right, new[] { sample }, weights, .1f);
            Assert.IsFalse(template.TryValidate(out report));
            StringAssert.Contains("Samples[0]/Joints[10]/IndexTip", report.ToString());
        }

        [TestCase("", "ok")] [TestCase("  ", "ok")] [TestCase(" id", "ok")] [TestCase("id ", "ok")] [TestCase("id", "  ")]
        public void InvalidIdentityAndDisplayName_Fail(string id, string name)
        {
            var template = Template();
            template.SetDefinition(id, name, HandSide.Right, template.CopySamples(), template.CopyJointWeights(), .1f);
            Assert.IsFalse(template.TryValidate(out _));
        }

        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidWeightsAndThreshold_Fail(float bad)
        {
            var template = Template();
            var weights = template.CopyJointWeights();
            weights[5] = bad;
            template.SetDefinition(template.PoseId, "Synthetic", HandSide.Right, template.CopySamples(), weights, .1f);
            Assert.IsFalse(template.TryValidate(out var report));
            StringAssert.Contains("JointWeights[5]/ThumbTip", report.ToString());
            template.SetDefinition(template.PoseId, "Synthetic", HandSide.Right, template.CopySamples(), HandPoseData.CreateDefaultWeights(), bad);
            Assert.IsFalse(template.TryValidate(out report));
            StringAssert.Contains("MaxError", report.ToString());
        }

        [Test]
        public void EmptyAllZeroMalformedAndOverBudget_FailWithoutTruncation()
        {
            var template = Template();
            var samples = template.CopySamples();
            template.SetDefinition("id", "Synthetic", HandSide.Right, samples, new float[26], .1f);
            Assert.IsFalse(template.TryValidate(out _));
            template.SetDefinition("id", "Synthetic", HandSide.Right, samples, new float[25], .1f);
            Assert.IsFalse(template.TryValidate(out _));
            template.SetDefinition("id", "Synthetic", HandSide.Right, new PoseSample[0], HandPoseData.CreateDefaultWeights(), .1f);
            Assert.IsFalse(template.TryValidate(out _));
            template.SetDefinition("id", "Synthetic", HandSide.Right, new[] { samples[0], samples[0] }, HandPoseData.CreateDefaultWeights(), .1f, 1);
            Assert.IsFalse(template.TryValidate(out var report));
            Assert.AreEqual(2, template.SampleCount);
            StringAssert.Contains("exceed storage budget", report.ToString());
            template.SetDefinition("id", "Synthetic", HandSide.Right, samples, HandPoseData.CreateDefaultWeights(), .1f, 0);
            Assert.IsFalse(template.TryValidate(out _));
        }

        [TestCase("m_SchemaVersion")] [TestCase("m_JointLayoutVersion")] [TestCase("m_NormalizationVersion")]
        public void UnsupportedTemplateVersions_Fail(string field)
        {
            var template = Template();
            SetField(template, field, 2);
            Assert.IsFalse(template.TryValidate(out _));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void InvalidSerializedSamples_FailWithSamplePaths(int kind)
        {
            var template = Template();
            var sample = Sample();
            if (kind == 0) SetField(sample, "m_HandSide", HandSide.Left);
            if (kind == 1) SetField(sample, "m_Space", HandFrameSpace.TrackingSpace);
            if (kind == 2) SetField(sample, "m_JointLayoutVersion", 2);
            if (kind == 3) SetField(sample, "m_NormalizationVersion", 0);
            if (kind == 4) SetField(sample, "m_SchemaVersion", 0);
            if (kind == 5) SetField(sample, "m_SourceTimestampSeconds", double.NaN);
            if (kind == 6) SetField(sample, "m_Joints", new JointPose[25]);
            if (kind >= 7 && kind <= 8)
            {
                var joints = sample.CopyJoints();
                if (kind == 7) joints[5].JointId = HandJointId.Wrist;
                else joints[5].Position = new Vector3(float.NaN, 0, 0);
                SetField(sample, "m_Joints", joints);
            }
            template.SetDefinition("id", "Synthetic", HandSide.Right, new[] { kind == 9 ? null : sample }, HandPoseData.CreateDefaultWeights(), .1f);
            Assert.IsFalse(template.TryValidate(out var report));
            StringAssert.Contains("Samples[0]", report.ToString());
        }

        [Test]
        public void InvalidRotations_KeepUsablePositions_AndBecomeUnavailableInSnapshot()
        {
            var sample = Sample();
            var joints = sample.CopyJoints();
            joints[5].Rotation = new Quaternion(float.NaN, 0, 0, 0);
            SetField(sample, "m_Joints", joints);
            var template = Template();
            template.SetDefinition("id", "Synthetic", HandSide.Right, new[] { sample }, HandPoseData.CreateDefaultWeights(), .1f);
            var db = Create<PoseDatabase>();
            db.SetTemplates(new[] { template });
            Assert.IsTrue(db.TryBuildSnapshot(out var snapshot, out var report), report.ToString());
            Assert.IsTrue(snapshot.Templates[0].Samples[0].TryGetJoint(HandJointId.ThumbTip, out var joint));
            Assert.IsFalse(joint.HasRotation);
        }

        [Test]
        public void EmptyDatabase_IsValid_UnsupportedSchemaAndNullArrayFail()
        {
            var db = Create<PoseDatabase>();
            Assert.IsTrue(db.TryBuildSnapshot(out var empty, out var report), report.ToString());
            Assert.AreEqual(0, empty.PoseCount);
            Assert.AreEqual(0, empty.GetCandidates(HandSide.Right).Count);
            Assert.IsFalse(empty.TryGetPose(null, out _));
            SetField(db, "m_SchemaVersion", 0);
            Assert.IsFalse(db.TryBuildSnapshot(out var failed, out _));
            Assert.IsNull(failed);
            SetField(db, "m_SchemaVersion", 1);
            db.SetTemplates(null);
            Assert.IsFalse(db.TryBuildSnapshot(out _, out _));
        }

        [Test]
        public void OrdinalIdsAndSideBuckets_AreDeterministicAndCached()
        {
            var db = Create<PoseDatabase>();
            db.SetTemplates(new[] { Template("z"), Template("left", HandSide.Left), Template("A"), Template("a") });
            Assert.IsTrue(db.TryBuildSnapshot(out var snapshot, out var report), report.ToString());
            Assert.AreEqual(4, snapshot.PoseCount);
            Assert.AreEqual(4, snapshot.SampleCount);
            Assert.AreEqual("A", snapshot.Templates[0].PoseId);
            Assert.AreEqual("a", snapshot.Templates[1].PoseId);
            Assert.AreEqual("z", snapshot.Templates[3].PoseId);
            Assert.AreEqual(1, snapshot.GetCandidates(HandSide.Left).Count);
            Assert.AreEqual(3, snapshot.GetCandidates(HandSide.Right).Count);
            Assert.AreSame(snapshot.GetCandidates(HandSide.Right), snapshot.GetCandidates(HandSide.Right));
            Assert.AreEqual(0, snapshot.GetCandidates(HandSide.Unknown).Count);
            Assert.IsTrue(snapshot.TryGetPose("A", out var upper));
            Assert.AreEqual(HandSide.Right, upper.HandSide);
            Assert.IsTrue(snapshot.TryGetPose("a", out var lower));
            Assert.AreEqual(HandSide.Right, lower.HandSide);
            Assert.IsTrue(snapshot.TryGetPose("left", out var left));
            Assert.AreEqual(HandSide.Left, left.HandSide);
        }

        [Test]
        public void DuplicateNullAndInvalidChild_FailAtomically_WithPaths()
        {
            var db = Create<PoseDatabase>();
            var a = Template("same");
            var b = Template("same", HandSide.Left);
            db.SetTemplates(new[] { a, b });
            Assert.IsFalse(db.TryBuildSnapshot(out var snapshot, out var report));
            Assert.IsNull(snapshot);
            StringAssert.Contains("Duplicate ordinal ID", report.ToString());
            db.SetTemplates(new HandPoseData[] { null });
            Assert.IsFalse(db.TryBuildSnapshot(out snapshot, out report));
            StringAssert.Contains("Templates[0]", report.ToString());
            SetField(a, "m_HandSide", HandSide.Unknown);
            db.SetTemplates(new[] { a });
            Assert.IsFalse(db.TryBuildSnapshot(out snapshot, out report));
            Assert.IsNull(snapshot);
            StringAssert.Contains("HandSide", report.ToString());
        }

        [Test]
        public void ExplicitRebuild_AssetEditsCannotMutatePublishedSnapshot()
        {
            var db = Create<PoseDatabase>();
            var template = Template("original");
            var input = new[] { template };
            db.SetTemplates(input);
            input[0] = null;
            Assert.IsTrue(db.TryBuildSnapshot(out var old, out var report), report.ToString());
            var samples = template.CopySamples();
            var joints = samples[0].CopyJoints();
            joints[10].Position = Vector3.one * 9;
            SetField(samples[0], "m_Joints", joints);
            template.SetDefinition("changed", "New display", HandSide.Right, samples, HandPoseData.CreateDefaultWeights(), .9f);
            Assert.IsTrue(db.TryBuildSnapshot(out var rebuilt, out report), report.ToString());
            Assert.IsTrue(old.TryGetPose("original", out var retained));
            Assert.IsFalse(old.TryGetPose("changed", out _));
            Assert.AreEqual(.1f, retained.MaxError);
            retained.Samples[0].TryGetJoint(HandJointId.IndexTip, out var original);
            Assert.AreNotEqual(joints[10].Position, original.Position);
            Assert.IsTrue(rebuilt.TryGetPose("changed", out var changed));
            changed.Samples[0].TryGetJoint(HandJointId.IndexTip, out var newer);
            Assert.AreEqual(joints[10].Position, newer.Position);
            var copy = retained.Samples[0].CopyJoints();
            copy[10].Position = Vector3.zero;
            retained.Samples[0].TryGetJoint(HandJointId.IndexTip, out var unchanged);
            Assert.AreEqual(original.Position, unchanged.Position);
            Assert.Throws<NotSupportedException>(() => ((IList<float>)retained.JointWeights)[0] = 2);
            Assert.Throws<NotSupportedException>(() => ((IList<PoseTemplate>)old.Templates)[0] = changed);
            SetField(template, "m_MaxError", -1f);
            Assert.IsFalse(db.TryBuildSnapshot(out var failed, out _));
            Assert.IsNull(failed);
            Assert.IsTrue(old.TryGetPose("original", out _));
        }
    }
}
