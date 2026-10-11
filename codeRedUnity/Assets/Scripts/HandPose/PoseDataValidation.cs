using System;
using System.Collections.Generic;

namespace HandPoseRecognition
{
    public sealed class ValidationReport
    {
        readonly List<string> m_Errors = new List<string>();
        readonly System.Collections.ObjectModel.ReadOnlyCollection<string> m_ReadOnlyErrors;
        public ValidationReport() { m_ReadOnlyErrors = m_Errors.AsReadOnly(); }
        public IReadOnlyList<string> Errors => m_ReadOnlyErrors;
        public bool IsValid => m_Errors.Count == 0;
        internal void Add(string path, string message) => m_Errors.Add($"{path}: {message}");
        public override string ToString() => IsValid ? "Valid" : string.Join("\n", m_Errors);
    }

    // One validation path is shared by conversion, standalone templates and catalog builds.
    internal static class PoseDataValidation
    {
        internal static bool KnownSide(HandSide side) => side == HandSide.Left || side == HandSide.Right;

        internal static void ValidateWeights(float[] weights, string path, ValidationReport report)
        {
            if (weights == null || weights.Length != HandFrame.JointCount)
            { report.Add(path, "Requires exactly 26 weights in frozen joint order."); return; }
            bool positive = false;
            for (int i = 0; i < weights.Length; i++)
            {
                if (!PoseMath.IsFinite(weights[i]) || weights[i] < 0)
                    report.Add($"{path}[{i}]/{(HandJointId)i}", "Weight must be finite and nonnegative.");
                else positive |= weights[i] > 0;
            }
            if (!positive) report.Add(path, "At least one weight must be positive.");
        }

        internal static void ValidateTemplate(HandPoseData asset, string path, ValidationReport report)
        {
            if (asset == null) { report.Add(path, "Null template reference."); return; }
            if (asset.SchemaVersion != 1) report.Add(path + "/SchemaVersion", "Only schema 1 is supported.");
            if (string.IsNullOrWhiteSpace(asset.PoseId) || !string.Equals(asset.PoseId, asset.PoseId.Trim(), StringComparison.Ordinal))
                report.Add(path + "/PoseId", "Requires a nonblank ordinal ID with no leading/trailing whitespace.");
            if (string.IsNullOrWhiteSpace(asset.DisplayName)) report.Add(path + "/DisplayName", "Required nonblank display text.");
            if (!KnownSide(asset.HandSide)) report.Add(path + "/HandSide", "Requires Left or Right.");
            if (asset.JointLayoutVersion != 1) report.Add(path + "/JointLayoutVersion", "Only layout 1 is supported.");
            if (asset.NormalizationVersion != 1) report.Add(path + "/NormalizationVersion", "Only canonical normalization 1 is supported.");
            if (!PoseMath.IsFinite(asset.MaxError) || asset.MaxError < 0)
                report.Add(path + "/MaxError", "Inclusive normalized palm-width threshold must be finite and nonnegative.");
            if (asset.MaxSamplesPerPose < 1) report.Add(path + "/MaxSamplesPerPose", "Storage budget must be positive.");
            ValidateWeights(asset.SerializedWeights, path + "/JointWeights", report);
            var samples = asset.SerializedSamples;
            if (samples == null || samples.Length == 0) { report.Add(path + "/Samples", "At least one sample is required."); return; }
            if (samples.Length > asset.MaxSamplesPerPose)
                report.Add(path + "/Samples", $"{samples.Length} samples exceed storage budget {asset.MaxSamplesPerPose}; no truncation performed.");
            for (int i = 0; i < samples.Length; i++)
                ValidateSample(samples[i], asset.HandSide, asset.JointLayoutVersion, asset.NormalizationVersion,
                    asset.SerializedWeights, $"{path}/Samples[{i}]", report);
        }

        internal static void ValidateSample(PoseSample sample, HandSide side, int layout, int normalization,
            float[] weights, string path, ValidationReport report)
        {
            if (sample == null) { report.Add(path, "Null sample."); return; }
            if (sample.SchemaVersion != 1) report.Add(path + "/SchemaVersion", "Only sample schema 1 is supported.");
            if (!KnownSide(sample.HandSide) || sample.HandSide != side) report.Add(path + "/HandSide", "Sample side must match its template's known side.");
            if (sample.Space != HandFrameSpace.CanonicalHandSpace) report.Add(path + "/Space", "Samples must be canonical; normalize raw frames before conversion.");
            if (sample.JointLayoutVersion != 1 || sample.JointLayoutVersion != layout) report.Add(path + "/JointLayoutVersion", "Sample must match supported template layout 1.");
            if (sample.NormalizationVersion != 1 || sample.NormalizationVersion != normalization) report.Add(path + "/NormalizationVersion", "Sample must match supported canonical normalization 1.");
            if (!PoseMath.IsFinite(sample.SourceTimestampSeconds) || sample.SourceTimestampSeconds < 0)
                report.Add(path + "/SourceTimestampSeconds", "Provenance timestamp must be finite and nonnegative.");
            var joints = sample.SerializedJoints;
            if (joints == null || joints.Length != HandFrame.JointCount)
            { report.Add(path + "/Joints", "Requires exactly 26 joint slots."); return; }
            for (int i = 0; i < joints.Length; i++)
            {
                var jointPath = $"{path}/Joints[{i}]/{(HandJointId)i}";
                if (joints[i].JointId != (HandJointId)i) report.Add(jointPath, "Misordered or duplicate joint ID.");
                if (!PoseMath.IsFinite(joints[i].Position)) report.Add(jointPath, "Position/placeholder must be finite.");
                if (weights != null && weights.Length == HandFrame.JointCount && weights[i] > 0 && !joints[i].HasPosition)
                    report.Add(jointPath, "Positive-weight joint requires a valid position in every sample.");
                // Rotations are optional for position matching. Snapshot copies sanitize invalid rotations to unavailable.
            }
        }
    }
}
