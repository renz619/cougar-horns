using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public readonly struct GestureFeaturePoint
    {
        public readonly double Time;
        public readonly Vector3 Tip, Wrist;
        public readonly Quaternion WristRotation;
        public GestureFeaturePoint(double time, Vector3 tip, Vector3 wrist, Quaternion rotation)
        { Time = time; Tip = tip; Wrist = wrist; WristRotation = rotation; }
    }

    public sealed class GestureFeatureSequence
    {
        internal readonly GestureFeaturePoint[] Points;
        public IReadOnlyList<GestureFeaturePoint> Samples { get; }
        public double DurationSeconds { get; }
        public double PathLength { get; }
        public double Excursion { get; }
        internal GestureFeatureSequence(GestureFeaturePoint[] points, double duration, double length, double excursion)
        { Points = points; Samples = Array.AsReadOnly(points); DurationSeconds = duration; PathLength = length; Excursion = excursion; }
    }

    /// <summary>Feature/preprocessing v1: fixed tracking-space basis and initial palm scale; never moving-wrist normalization.</summary>
    public static class GestureFeatureExtractor
    {
        public static bool TryFreezeReference(GestureObservation observation, GestureDefinition definition, GestureCalibration calibration,
            out GestureReference reference, out string reason)
        {
            reference = null; reason = null;
            if (definition == null || !definition.TryValidate(out var report))
            { reason = "Reference/Definition: invalid or missing definition."; return false; }
            if (!definition.IsSupported) { reason = "Reference/Category: one right-dominant J/Z hand is supported."; return false; }
            if (observation == null || !observation.TryValidate(out reason)) { reason = reason ?? "Reference/Observation: unavailable."; return false; }
            if (calibration == null) { reason = "Reference/Calibration: unavailable."; return false; }
            if (!calibration.TryGetBasis(out var basis, out reason)) return false;
            if (calibration.CoordinateEpoch != observation.CoordinateEpoch) { reason = "Reference/Epoch: calibration and acquisition differ."; return false; }
            var frame = observation.GetHand(definition.DominantSide);
            if (!TryValidateHand(frame, definition, out reason)) return false;
            frame.TryGetJoint(HandJointId.IndexProximal, out var index); frame.TryGetJoint(HandJointId.LittleProximal, out var little);
            frame.TryGetJoint(definition.TrajectoryJoint, out var tip); frame.TryGetJoint(HandJointId.Wrist, out var wrist);
            float scale = (index.Position - little.Position).magnitude;
            if (!PoseMath.IsFinite(scale) || scale < HandPoseNormalizer.MinimumPalmWidthMeters)
            { reason = "Reference/PalmWidth: degenerate or nonfinite."; return false; }
            reference = new GestureReference(observation, definition.DominantSide, basis, scale, tip.Position, wrist.Position);
            return true;
        }

        public static bool TryValidateHand(HandFrame hand, GestureDefinition definition, out string reason)
        {
            reason = null;
            if (hand == null || !hand.IsTracked || !hand.IsValid || hand.HandSide != definition.DominantSide
                || hand.Space != HandFrameSpace.TrackingSpace || hand.JointLayoutVersion != 1 || hand.NormalizationVersion != 0)
            { reason = "Hand: tracked raw evidence from the configured physical side is required."; return false; }
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                if (i == (int)HandJointId.Palm) continue;
                if (!hand.TryGetJoint((HandJointId)i, out var joint) || !PoseMath.IsFinite(joint.Position))
                { reason = "Hand/Joints/" + (HandJointId)i + ": required position unavailable."; return false; }
            }
            hand.TryGetJoint(HandJointId.Wrist, out var wrist);
            if ((definition.MotionKind == GestureMotionKind.J || definition.Settings.OrientationWeight > 0)
                && (!wrist.HasRotation || !PoseMath.TryNormalize(wrist.Rotation, out _)))
            { reason = "Hand/Joints/Wrist: required orientation unavailable."; return false; }
            return true;
        }

        public static bool TryValidateObservation(GestureObservation observation, GestureDefinition definition, GestureReference reference,
            GestureObservation previous, out string reason)
        {
            reason = null;
            if (observation == null || !observation.TryValidate(out reason)) { reason = reason ?? "Missing acquisition."; return false; }
            if (reference == null || observation.SourceIdentity != reference.SourceIdentity || observation.CoordinateEpoch != reference.CoordinateEpoch
                || definition.DominantSide != reference.DominantSide)
            { reason = "Source, dominance or coordinate epoch changed."; return false; }
            var hand = observation.GetHand(definition.DominantSide);
            if (!TryValidateHand(hand, definition, out reason)) return false;
            // This one-hand producer must publish the current hand's actual acquisition time.
            if (hand.TimestampSeconds != observation.TimestampSeconds)
            { reason = "One-hand observation time must equal its hand acquisition time."; return false; }
            if (previous == null) return true;
            var old = previous.GetHand(definition.DominantSide);
            double dt = observation.TimestampSeconds - previous.TimestampSeconds;
            if (old == null || observation.AcquisitionId <= previous.AcquisitionId || hand.SequenceId <= old.SequenceId || dt <= 0)
            { reason = "Acquisition, source sequence and timestamp must strictly advance."; return false; }
            if (dt > definition.Settings.MaxGapSeconds) { reason = "Tracking gap exceeds the configured maximum; interpolation is forbidden."; return false; }
            hand.TryGetJoint(definition.TrajectoryJoint, out var tip); old.TryGetJoint(definition.TrajectoryJoint, out var oldTip);
            hand.TryGetJoint(HandJointId.Wrist, out var wrist); old.TryGetJoint(HandJointId.Wrist, out var oldWrist);
            double speed = Math.Max(Distance(tip.Position, oldTip.Position), Distance(wrist.Position, oldWrist.Position)) / reference.PalmWidth / dt;
            if (!PoseMath.IsFinite(speed) || speed > definition.Settings.MaxSpeedPalmWidthsPerSecond)
            { reason = "Implausible fingertip/wrist jump; do not smooth across discontinuities."; return false; }
            return true;
        }

        public static bool TryExtract(GestureClip clip, out GestureFeatureSequence sequence, out string reason)
            => TryExtract(clip, clip?.FrozenDefinition, out sequence, out reason);

        internal static bool TryExtract(GestureClip clip, GestureDefinition definition, out GestureFeatureSequence sequence, out string reason)
        {
            sequence = null; reason = null;
            if (clip == null) { reason = "No clip."; return false; }
            if (!clip.TryValidate(out var report)) { reason = report.ToString(); return false; }
            if (definition == null || !definition.IsSupported || clip.Observations.Count < 2)
            { reason = "At least two distinct acquisitions of a supported motion are required."; return false; }
            var settings = definition.Settings;
            int count = clip.Observations.Count;
            var smooth = new GestureFeaturePoint[count];
            var inverse = Quaternion.Inverse(clip.Reference.Basis);
            var first = clip.Observations[0].GetHand(definition.DominantSide);
            first.TryGetJoint(definition.TrajectoryJoint, out var firstTip); first.TryGetJoint(HandJointId.Wrist, out var firstWrist);
            double length = 0, excursion = 0;
            for (int i = 0; i < count; i++)
            {
                var observation = clip.Observations[i];
                if (!TryValidateObservation(observation, definition, clip.Reference, i == 0 ? null : clip.Observations[i - 1], out reason)) return false;
                var hand = observation.GetHand(definition.DominantSide);
                hand.TryGetJoint(definition.TrajectoryJoint, out var tip); hand.TryGetJoint(HandJointId.Wrist, out var wrist);
                var tipPath = inverse * ((tip.Position - firstTip.Position) / clip.Reference.PalmWidth);
                var wristPath = inverse * ((wrist.Position - firstWrist.Position) / clip.Reference.PalmWidth);
                // Orientation is absolute in the frozen reference: inverse(startBasis) * rawWrist.
                var rotation = inverse * wrist.Rotation;
                if (!PoseMath.IsFinite(tipPath) || !PoseMath.IsFinite(wristPath) || !PoseMath.TryNormalize(rotation, out rotation))
                { reason = "Extracted path/orientation is nonfinite."; return false; }
                double time = observation.TimestampSeconds - clip.StartTime;
                if (i > 0)
                {
                    float alpha = settings.SmoothingSeconds == 0 ? 1 : (float)(1 - Math.Exp(-(time - smooth[i - 1].Time) / settings.SmoothingSeconds));
                    tipPath = Vector3.LerpUnclamped(smooth[i - 1].Tip, tipPath, alpha);
                    wristPath = Vector3.LerpUnclamped(smooth[i - 1].Wrist, wristPath, alpha);
                    rotation = Quaternion.SlerpUnclamped(smooth[i - 1].WristRotation, rotation, alpha);
                    length += Distance(smooth[i - 1].Tip, tipPath);
                }
                excursion = Math.Max(excursion, Distance(Vector3.zero, tipPath));
                smooth[i] = new GestureFeaturePoint(time, tipPath, wristPath, rotation);
            }
            var points = new GestureFeaturePoint[settings.ResampleCount];
            int segment = 0;
            for (int i = 0; i < points.Length; i++)
            {
                double time = clip.DurationSeconds * i / (points.Length - 1);
                while (segment < count - 2 && smooth[segment + 1].Time < time) segment++;
                var a = smooth[segment]; var b = smooth[segment + 1];
                float t = (float)((time - a.Time) / (b.Time - a.Time));
                points[i] = new GestureFeaturePoint(time, Vector3.LerpUnclamped(a.Tip, b.Tip, t),
                    Vector3.LerpUnclamped(a.Wrist, b.Wrist, t), Quaternion.SlerpUnclamped(a.WristRotation, b.WristRotation, t));
            }
            sequence = new GestureFeatureSequence(points, clip.DurationSeconds, length, excursion);
            return true;
        }

        internal static double Distance(Vector3 a, Vector3 b)
        { double x = (double)a.x - b.x, y = (double)a.y - b.y, z = (double)a.z - b.z; return Math.Sqrt(x * x + y * y + z * z); }
    }
}
