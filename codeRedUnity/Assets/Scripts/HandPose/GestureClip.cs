using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>One ordered performance, with raw evidence and its frozen shared reference.</summary>
    [Serializable]
    public sealed class GestureClip
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] GestureDefinition m_Definition;
        [SerializeField] GestureReference m_Reference;
        [SerializeField] GestureObservation[] m_Observations;
        [NonSerialized] IReadOnlyList<GestureObservation> m_View;
        public int SchemaVersion => m_SchemaVersion;
        public GestureDefinition Definition => m_Definition?.Copy();
        internal GestureDefinition FrozenDefinition => m_Definition;
        public GestureReference Reference => m_Reference;
        public IReadOnlyList<GestureObservation> Observations => m_View ?? (m_View = Array.AsReadOnly(m_Observations ?? Array.Empty<GestureObservation>()));
        public double StartTime => Observations.Count == 0 ? 0 : Observations[0]?.TimestampSeconds ?? 0;
        public double DurationSeconds => Observations.Count < 2 ? 0 : (Observations[Observations.Count - 1]?.TimestampSeconds ?? StartTime) - StartTime;
        public double RelativeTime(int index) => Observations[index].TimestampSeconds - StartTime;

        public GestureClip(GestureDefinition definition, GestureReference reference, GestureObservation[] observations)
        {
            m_Definition = definition?.Copy(); m_Reference = reference?.Clone();
            // Observations and HandFrames are immutable. Own the array, share its immutable values.
            m_Observations = observations == null ? null : (GestureObservation[])observations.Clone();
        }
        public GestureClip Clone()
        { var copy = new GestureClip(m_Definition, m_Reference, m_Observations); copy.m_SchemaVersion = m_SchemaVersion; return copy; }
        public bool TryValidate(out ValidationReport report)
        {
            report = new ValidationReport();
            if (SchemaVersion != 1) report.Add("Clip/SchemaVersion", "Only schema 1 is supported.");
            if (m_Definition == null) { report.Add("Clip/Definition", "Required."); return false; }
            if (!m_Definition.TryValidate(out var definitionReport))
                foreach (var error in definitionReport.Errors) report.Add("Clip", error);
            if (!m_Definition.IsSupported) report.Add("Clip/Category", "This producer supports right-dominant one-hand J/Z only.");
            if (m_Reference == null) { report.Add("Clip/Reference", "Required."); return false; }
            if (!m_Reference.TryValidate(out var reason)) report.Add("Clip/Reference", reason);
            if (!report.IsValid) return false;
            if (m_Observations == null || m_Observations.Length == 0 || m_Observations.Length > m_Definition.Settings.MaxFrames)
            { report.Add("Clip/Frames", "Nonempty frame array within the configured cap required."); return false; }
            GestureObservation previous = null;
            for (int i = 0; i < m_Observations.Length; i++)
            {
                if (!GestureFeatureExtractor.TryValidateObservation(m_Observations[i], m_Definition, m_Reference, previous, out reason))
                    report.Add($"Clip/Frames[{i}]", reason);
                previous = m_Observations[i];
            }
            if (DurationSeconds > m_Definition.Settings.MaxDurationSeconds) report.Add("Clip/Duration", "Complete clip exceeds duration budget.");
            return report.IsValid;
        }
    }

    /// <summary>Reserved simultaneous static arrangement. No trajectory and no DTW are required.</summary>
    [Serializable]
    public sealed class GestureStaticSample
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] PoseSample m_DominantShape, m_NonDominantShape;
        [SerializeField] Vector3 m_RelativePalmPosition;
        [SerializeField] Quaternion m_RelativeWristRotation = Quaternion.identity;
        [SerializeField] HandSide m_DominantSide;
        public int SchemaVersion => m_SchemaVersion;
        public HandSide DominantSide => m_DominantSide;
        public Vector3 RelativePalmPosition => m_RelativePalmPosition;
        public Quaternion RelativeWristRotation => m_RelativeWristRotation;
        public PoseSample DominantShape => m_DominantShape?.Clone();
        public PoseSample NonDominantShape => m_NonDominantShape?.Clone();
        public GestureStaticSample(PoseSample dominant, PoseSample nonDominant, HandSide side, Vector3 relativePosition, Quaternion relativeRotation)
        { m_DominantShape = dominant?.Clone(); m_NonDominantShape = nonDominant?.Clone(); m_DominantSide = side; m_RelativePalmPosition = relativePosition; m_RelativeWristRotation = relativeRotation; }
        public GestureStaticSample Clone()
        { var copy = new GestureStaticSample(m_DominantShape, m_NonDominantShape, m_DominantSide, m_RelativePalmPosition, m_RelativeWristRotation); copy.m_SchemaVersion = m_SchemaVersion; return copy; }
        internal void Validate(GestureDefinition definition, string path, ValidationReport report)
        {
            if (m_SchemaVersion != 1 || m_DominantSide != definition.DominantSide || !PoseMath.IsFinite(m_RelativePalmPosition)
                || !PoseMath.TryNormalize(m_RelativeWristRotation, out _)) report.Add(path, "Invalid simultaneous arrangement metadata.");
            var weights = HandPoseData.CreateDefaultWeights();
            if ((definition.RequiredRoles & GestureHandRoles.Dominant) != 0)
                PoseDataValidation.ValidateSample(m_DominantShape, definition.DominantSide, 1, 1, weights, path + "/Dominant", report);
            if ((definition.RequiredRoles & GestureHandRoles.NonDominant) != 0)
                PoseDataValidation.ValidateSample(m_NonDominantShape, definition.NonDominantSide, 1, 1, weights, path + "/NonDominant", report);
        }
    }
}
