using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    // Test-owned geometry only. These are not reviewed ASL examples or a supplied training corpus.
    public static class GestureTestFixtures
    {
        public static GestureDefinition Definition(GestureMotionKind kind = GestureMotionKind.Z, string id = null)
            => new GestureDefinition { GestureId = id ?? "synthetic.motion." + kind, DisplayName = "SYNTHETIC " + kind,
                MotionKind = kind, Variant = "Synthetic software fixture", Provenance = "Generated test geometry; not ASL validation" };

        public static HandFrame Frame(GestureMotionKind kind, Vector3 tipPath, double time, ulong sequence,
            float wristAngle = 0, Quaternion? basis = null, Vector3? origin = null, float scale = 1, bool tracked = true, bool wrongShape = false)
        {
            var hand = FakeHandData.Hand(wrongShape ? 0 : kind == GestureMotionKind.Z ? 0 : 70, wrongShape ? 0 : 70,
                wrongShape ? 0 : 70, wrongShape ? 0 : kind == GestureMotionKind.J ? 0 : 70, Quaternion.identity, Vector3.zero);
            hand.thumb = new ThumbJoints { metacarpal = new Vector3(-.035f, 0, .015f), proximal = new Vector3(-.025f, 0, .035f),
                distal = new Vector3(-.01f, 0, .05f), tip = hand.index.proximal, isValid = true };
            var joints = HandFrame.CreateUnavailableJoints();
            void Set(int slot, Vector3 p) => joints[slot] = new JointPose((HandJointId)slot, p, Quaternion.identity, true, true);
            void Finger(int slot, FingerJoints f)
            { Set(slot, f.metacarpal); Set(slot + 1, f.proximal); Set(slot + 2, f.intermediate); Set(slot + 3, f.distal); Set(slot + 4, f.tip); }
            Set(0, new Vector3(.03f, -.025f, 0)); Set(1, new Vector3(.03f, 0, .03f));
            Set(2, hand.thumb.metacarpal); Set(3, hand.thumb.proximal); Set(4, hand.thumb.distal); Set(5, hand.thumb.tip);
            Finger(6, hand.index); Finger(11, hand.middle); Finger(16, hand.ring); Finger(21, hand.little);
            var rotation = Quaternion.AngleAxis(wristAngle, Vector3.forward); var reference = basis ?? Quaternion.identity;
            Vector3 initialTip = joints[(int)(kind == GestureMotionKind.J ? HandJointId.LittleTip : HandJointId.IndexTip)].Position;
            Vector3 translation = initialTip + tipPath * .06f - rotation * initialTip;
            for (int i = 0; i < joints.Length; i++)
            { joints[i].Position = (origin ?? Vector3.zero) + reference * ((rotation * joints[i].Position + translation) * scale); joints[i].Rotation = reference * rotation; }
            return new HandFrame(time, sequence, HandSide.Right, tracked, HandFrameSpace.TrackingSpace, joints);
        }
        public static GestureObservation Observation(HandFrame frame, string source = "synthetic-source", ulong epoch = 1)
            => new GestureObservation(frame.TimestampSeconds, frame.SequenceId, source, epoch, right: frame);
        public static GestureObservation[] Observations(GestureMotionKind kind = GestureMotionKind.Z, double start = 10, double duration = 1,
            Quaternion? basis = null, Vector3? origin = null, float scale = 1, bool reverse = false, bool wrongShape = false, ulong firstSequence = 1)
        {
            var vertices = kind == GestureMotionKind.Z
                ? new[] { Vector3.zero, new Vector3(.8f, 0, 0), new Vector3(0, -.8f, 0), new Vector3(.8f, -.8f, 0) }
                : new[] { Vector3.zero, new Vector3(0, -.8f, 0), new Vector3(.15f, -1, 0), new Vector3(.45f, -.95f, 0), new Vector3(.65f, -.6f, 0) };
            var paths = new List<Vector3> { vertices[0] };
            for (int segment = 0; segment < vertices.Length - 1; segment++)
                for (int i = 1; i <= 10; i++) paths.Add(Vector3.Lerp(vertices[segment], vertices[segment + 1], i / 10f));
            if (reverse) paths.Reverse();
            for (int i = 0; i < 12; i++) paths.Add(paths[paths.Count - 1]);
            var observations = new GestureObservation[paths.Count];
            for (int i = 0; i < observations.Length; i++)
            {
                float progress = Math.Min(1, (float)i / (observations.Length - 13));
                observations[i] = Observation(Frame(kind, paths[i], start + duration * i / (observations.Length - 1), firstSequence + (ulong)i,
                    kind == GestureMotionKind.J ? 60 * progress : 0, basis, origin, scale, wrongShape: wrongShape));
            }
            return observations;
        }
        public static GestureClip Clip(GestureDefinition definition = null, GestureObservation[] observations = null, Quaternion? basis = null)
        {
            definition = definition ?? Definition(); observations = observations ?? Observations(definition.MotionKind);
            Assert.IsTrue(GestureFeatureExtractor.TryFreezeReference(observations[0], definition,
                new GestureCalibration(basis ?? Quaternion.identity, 1, observations[0].CoordinateEpoch), out var reference, out var reason), reason);
            return new GestureClip(definition, reference, observations);
        }
        public static GestureData Asset(GestureDefinition definition, params GestureClip[] clips)
        { var asset = ScriptableObject.CreateInstance<GestureData>(); asset.SetDefinition(definition, clips); return asset; }
        public static GestureCatalogSnapshot Catalog(params GestureData[] assets)
        {
            var catalog = ScriptableObject.CreateInstance<GestureCatalog>();
            try { catalog.SetEntries(assets); Assert.IsTrue(catalog.TryBuildSnapshot(out var snapshot, out var report), report.ToString()); return snapshot; }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
    }
}
