using System;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum GestureCategory { Static, Motion }
    public enum GestureMotionKind { Unsupported, J, Z }
    [Flags] public enum GestureHandRoles { None = 0, Dominant = 1, NonDominant = 2 }
    [Flags] public enum GestureHandMask { None = 0, Left = 1, Right = 2 }
    public enum GestureThumbPolicy { NotExtended, Curled }

    /// <summary>Prototype tuning, not calibrated ASL accuracy. Copy at every session/catalog boundary.</summary>
    [Serializable]
    public sealed class GestureMotionSettings
    {
        public double MinDurationSeconds = .15, MaxDurationSeconds = 6;
        public int MaxFrames = 768, MaxAttempts = 1536, ResampleCount = 64, DtwBand = 16, MaxWarpRun = 3;
        public double MaxGapSeconds = .12, MaxFrameAgeSeconds = .25, SmoothingSeconds = .025;
        public double MaxError = .4, MinExcursion = .45, MinPathLength = .8, MaxSpeedPalmWidthsPerSecond = 40;
        public double TipWeight = 1, WristWeight = .15, OrientationWeight = .15, OrientationScaleDegrees = 90;
        public double StrokeThreshold = .10;
        [Tooltip("Allowed vertical drift of Z's top and bottom strokes, in initial palm widths.")]
        public double StrokeVerticalTolerance = .35;
        public double EndPauseSeconds = .18, StartMovementThreshold = .12;
        public double PreMovementSeconds = .15, PauseMovementThreshold = .04;
        public int PreMovementFrames = 12;
        public double RearmSeconds = .2, CooldownSeconds = .5;
        public int SigningDirection = 1;
        public float ShapeExtendedMaxCurlDegrees = 70, ShapeCurledMinCurlDegrees = 160;
        public float FinishExtendedMaxCurlDegrees = 95, FinishCurledMinCurlDegrees = 140;
        public GestureThumbPolicy ThumbPolicy = GestureThumbPolicy.NotExtended, FinishThumbPolicy = GestureThumbPolicy.NotExtended;
        public float ThumbExtendedMinRatio = .75f, ThumbCurledMaxRatio = .45f;
        public double FinishPhaseStart = .65;
        public GestureMotionSettings Copy() => (GestureMotionSettings)MemberwiseClone();

        internal void Validate(ValidationReport report, string path)
        {
            Positive(MinDurationSeconds, nameof(MinDurationSeconds), report, path);
            Positive(MaxDurationSeconds, nameof(MaxDurationSeconds), report, path);
            if (MaxDurationSeconds < MinDurationSeconds) report.Add(path, "Duration bounds are reversed.");
            if (MaxFrames < 2 || MaxFrames > 4096 || MaxAttempts < 1 || MaxAttempts > 16384)
                report.Add(path, "Frame budget must be 2..4096; independent attempt budget must be 1..16384.");
            if (ResampleCount < 4 || ResampleCount > 256 || DtwBand < 0 || DtwBand >= ResampleCount || MaxWarpRun < 1 || MaxWarpRun > 16)
                report.Add(path, "Resampling must be 4..256; band must fit sample count; stretch limit must be 1..16.");
            Positive(MaxGapSeconds, nameof(MaxGapSeconds), report, path);
            Positive(MaxFrameAgeSeconds, nameof(MaxFrameAgeSeconds), report, path);
            Nonnegative(SmoothingSeconds, nameof(SmoothingSeconds), report, path);
            Nonnegative(MaxError, nameof(MaxError), report, path);
            Positive(MinExcursion, nameof(MinExcursion), report, path);
            Positive(MinPathLength, nameof(MinPathLength), report, path);
            Positive(MaxSpeedPalmWidthsPerSecond, nameof(MaxSpeedPalmWidthsPerSecond), report, path);
            Positive(TipWeight, nameof(TipWeight), report, path);
            Nonnegative(WristWeight, nameof(WristWeight), report, path);
            Nonnegative(OrientationWeight, nameof(OrientationWeight), report, path);
            if (!PoseMath.IsFinite(TipWeight + WristWeight + OrientationWeight))
                report.Add(path + "/FeatureWeights", "The complete feature-weight sum must remain finite.");
            Positive(OrientationScaleDegrees, nameof(OrientationScaleDegrees), report, path);
            Positive(StrokeThreshold, nameof(StrokeThreshold), report, path);
            Positive(StrokeVerticalTolerance, nameof(StrokeVerticalTolerance), report, path);
            Positive(EndPauseSeconds, nameof(EndPauseSeconds), report, path);
            Positive(StartMovementThreshold, nameof(StartMovementThreshold), report, path);
            Positive(PreMovementSeconds, nameof(PreMovementSeconds), report, path);
            Positive(PauseMovementThreshold, nameof(PauseMovementThreshold), report, path);
            if (PreMovementFrames < 2 || PreMovementFrames > MaxFrames || PreMovementSeconds >= MaxDurationSeconds
                || PauseMovementThreshold >= StartMovementThreshold || StartMovementThreshold >= MinExcursion)
                report.Add(path, "Onset history must fit the budget; pause < onset < full excursion thresholds required.");
            Nonnegative(RearmSeconds, nameof(RearmSeconds), report, path);
            Nonnegative(CooldownSeconds, nameof(CooldownSeconds), report, path);
            if (SigningDirection != 1 && SigningDirection != -1) report.Add(path, "SigningDirection must explicitly be +1 or -1.");
            if (!PoseMath.IsFinite(FinishPhaseStart) || FinishPhaseStart <= 0 || FinishPhaseStart >= 1)
                report.Add(path, "FinishPhaseStart must be strictly inside (0,1).");
            ValidateCurl(ShapeExtendedMaxCurlDegrees, ShapeCurledMinCurlDegrees, report, path + "/StartingShape");
            ValidateCurl(FinishExtendedMaxCurlDegrees, FinishCurledMinCurlDegrees, report, path + "/FinishShape");
            if (!PoseMath.IsFinite(ThumbExtendedMinRatio) || !PoseMath.IsFinite(ThumbCurledMaxRatio)
                || ThumbCurledMaxRatio < 0 || ThumbExtendedMinRatio <= ThumbCurledMaxRatio)
                report.Add(path, "Thumb ratios must be finite, nonnegative and ordered.");
            if (!Enum.IsDefined(typeof(GestureThumbPolicy), ThumbPolicy) || !Enum.IsDefined(typeof(GestureThumbPolicy), FinishThumbPolicy))
                report.Add(path, "Unsupported thumb policy.");
        }
        static void ValidateCurl(float extended, float curled, ValidationReport report, string path)
        {
            if (!PoseMath.IsFinite(extended) || !PoseMath.IsFinite(curled) || extended < 0 || curled > 360 || extended >= curled)
                report.Add(path, "Finite ordered curl thresholds in 0..360 required.");
        }
        static void Positive(double value, string field, ValidationReport report, string path)
        { if (!PoseMath.IsFinite(value) || value <= 0) report.Add(path + "/" + field, "Finite positive value required."); }
        static void Nonnegative(double value, string field, ValidationReport report, string path)
        { if (!PoseMath.IsFinite(value) || value < 0) report.Add(path + "/" + field, "Finite nonnegative value required."); }
    }

    /// <summary>Reserved simultaneous arrangement constraints; no two-hand evaluator is supplied in this milestone.</summary>
    [Serializable]
    public sealed class GestureSpatialConstraints
    {
        public bool RequireRelativePosition, RequireRelativeOrientation;
        public Vector3 RelativePosition;
        public float PositionTolerance = .25f, OrientationToleranceDegrees = 30;
        public Quaternion RelativeOrientation = Quaternion.identity;
        public HandJointId DominantProximityJoint = HandJointId.Palm, NonDominantProximityJoint = HandJointId.Palm;
        public bool RequireJointProximity;
        public float MinimumDistance, MaximumDistance = 2;
        public GestureSpatialConstraints Copy() => (GestureSpatialConstraints)MemberwiseClone();
        internal void Validate(ValidationReport report)
        {
            if (!PoseMath.IsFinite(RelativePosition) || !PoseMath.TryNormalize(RelativeOrientation, out _)
                || !PoseMath.IsFinite(PositionTolerance) || PositionTolerance < 0
                || !PoseMath.IsFinite(OrientationToleranceDegrees) || OrientationToleranceDegrees < 0 || OrientationToleranceDegrees > 180
                || !PoseMath.IsFinite(MinimumDistance) || !PoseMath.IsFinite(MaximumDistance) || MinimumDistance < 0 || MaximumDistance < MinimumDistance
                || (int)DominantProximityJoint < 0 || (int)DominantProximityJoint >= 26 || (int)NonDominantProximityJoint < 0 || (int)NonDominantProximityJoint >= 26)
                report.Add("Definition/SpatialConstraints", "Invalid finite spatial constraints or joint IDs.");
        }
    }

    [Serializable]
    public sealed class GestureDefinition
    {
        public int SchemaVersion = 1, FeatureVersion = 1, PreprocessingVersion = 1;
        public int MaxClips = 8;
        public string GestureId = "asl.letter.z", DisplayName = "Z";
        public GestureCategory Category = GestureCategory.Motion;
        public GestureMotionKind MotionKind = GestureMotionKind.Z;
        public GestureHandRoles RequiredRoles = GestureHandRoles.Dominant;
        public HandSide DominantSide = HandSide.Right;
        public bool AllowRoleExchange;
        public GestureMotionSettings Settings = new GestureMotionSettings();
        public GestureSpatialConstraints SpatialConstraints = new GestureSpatialConstraints();
        public string Variant = "Prototype", ReviewerStatus = "Not reviewed", Provenance = "Uncalibrated";
        public HandSide NonDominantSide => DominantSide == HandSide.Right ? HandSide.Left : DominantSide == HandSide.Left ? HandSide.Right : HandSide.Unknown;
        public HandJointId TrajectoryJoint => MotionKind == GestureMotionKind.J ? HandJointId.LittleTip : HandJointId.IndexTip;
        public bool IsSupported => Category == GestureCategory.Motion && (MotionKind == GestureMotionKind.J || MotionKind == GestureMotionKind.Z)
            && RequiredRoles == GestureHandRoles.Dominant && DominantSide == HandSide.Right && !AllowRoleExchange
            && SpatialConstraints != null && !SpatialConstraints.RequireRelativePosition
            && !SpatialConstraints.RequireRelativeOrientation && !SpatialConstraints.RequireJointProximity;
        public GestureDefinition Copy()
        {
            var copy = (GestureDefinition)MemberwiseClone();
            copy.Settings = Settings?.Copy(); copy.SpatialConstraints = SpatialConstraints?.Copy(); return copy;
        }
        public GestureDefinition Clone() => Copy();
        public bool TryValidate(out ValidationReport report)
        {
            report = new ValidationReport();
            if (SchemaVersion != 1 || FeatureVersion != 1 || PreprocessingVersion != 1) report.Add("Definition/Versions", "Only schema/feature/preprocessing version 1 is supported.");
            if (MaxClips < 1 || MaxClips > 32) report.Add("Definition/MaxClips", "Clip budget must be 1..32; no truncation is performed.");
            if (string.IsNullOrWhiteSpace(GestureId) || GestureId != GestureId.Trim()) report.Add("Definition/GestureId", "Nonblank trimmed ordinal ID required.");
            if (string.IsNullOrWhiteSpace(DisplayName)) report.Add("Definition/DisplayName", "Nonblank display name required.");
            if (!Enum.IsDefined(typeof(GestureCategory), Category) || !Enum.IsDefined(typeof(GestureMotionKind), MotionKind)) report.Add("Definition/Category", "Unknown category/motion kind.");
            if (RequiredRoles == GestureHandRoles.None || (RequiredRoles & ~(GestureHandRoles.Dominant | GestureHandRoles.NonDominant)) != 0)
                report.Add("Definition/Roles", "At least one known explicit role required.");
            if (!PoseDataValidation.KnownSide(DominantSide)) report.Add("Definition/Dominance", "Explicit physical dominance required.");
            if (Category == GestureCategory.Motion)
            { if (Settings == null) report.Add("Definition/Settings", "Motion settings required."); else Settings.Validate(report, "Definition/Settings"); }
            if (Category == GestureCategory.Motion && MotionKind == GestureMotionKind.J && Settings != null && Settings.OrientationWeight <= 0)
                report.Add("Definition/Settings/OrientationWeight", "J requires wrist-orientation evidence with positive weight.");
            if (SpatialConstraints == null) report.Add("Definition/SpatialConstraints", "Explicit spatial constraints required.");
            else SpatialConstraints.Validate(report);
            return report.IsValid;
        }
    }
}
