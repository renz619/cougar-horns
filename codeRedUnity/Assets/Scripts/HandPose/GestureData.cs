using System;
using UnityEngine;

namespace HandPoseRecognition
{
    [CreateAssetMenu(menuName = "Hand Gesture/Definition and complete clips")]
    public sealed class GestureData : ScriptableObject
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] GestureDefinition m_Definition = new GestureDefinition();
        [SerializeField] GestureClip[] m_Clips = Array.Empty<GestureClip>();
        [SerializeField] GestureStaticSample[] m_StaticSamples = Array.Empty<GestureStaticSample>();
        [SerializeField] HandPoseData m_DominantShape, m_NonDominantShape;
        public int SchemaVersion => m_SchemaVersion;
        public GestureDefinition Definition => m_Definition?.Copy();
        internal GestureDefinition FrozenDefinition => m_Definition;
        internal GestureClip[] Clips => m_Clips;
        internal HandPoseData DominantShape => m_DominantShape;
        internal HandPoseData NonDominantShape => m_NonDominantShape;
        public GestureClip[] CopyClips()
        {
            if (m_Clips == null) return null;
            var copy = new GestureClip[m_Clips.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = m_Clips[i]?.Clone();
            return copy;
        }
        public GestureStaticSample[] CopyStaticSamples()
        {
            if (m_StaticSamples == null) return null;
            var copy = new GestureStaticSample[m_StaticSamples.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = m_StaticSamples[i]?.Clone();
            return copy;
        }
        public void SetDefinition(GestureDefinition definition, GestureClip[] clips, GestureStaticSample[] staticSamples = null,
            HandPoseData dominantShape = null, HandPoseData nonDominantShape = null)
        {
            m_Definition = definition?.Copy(); m_Clips = clips == null ? null : (GestureClip[])clips.Clone();
            m_StaticSamples = staticSamples == null ? Array.Empty<GestureStaticSample>() : (GestureStaticSample[])staticSamples.Clone();
            m_DominantShape = dominantShape; m_NonDominantShape = nonDominantShape;
        }
        public bool TryValidate(out ValidationReport report)
        {
            report = new ValidationReport();
            if (m_SchemaVersion != 1) report.Add(name + "/SchemaVersion", "Only gesture schema 1 is supported.");
            if (m_Definition == null) { report.Add(name + "/Definition", "Required."); return false; }
            if (!m_Definition.TryValidate(out var definitionReport))
                foreach (var error in definitionReport.Errors) report.Add(name, error);
            if (!report.IsValid) return false;
            if (m_Clips == null || m_StaticSamples == null) { report.Add(name + "/Samples", "Explicit arrays required."); return false; }
            if (m_Clips.Length + m_StaticSamples.Length > m_Definition.MaxClips) report.Add(name + "/Samples", "Example budget exceeded.");
            if (m_Definition.IsSupported && m_Clips.Length == 0) report.Add(name + "/Clips", "At least one complete example required.");
            if (!m_Definition.IsSupported && m_Clips.Length > 0) report.Add(name + "/Clips", "Unsupported future motion cannot be imported as a J/Z clip.");
            if (m_Definition.Category == GestureCategory.Motion && m_StaticSamples.Length > 0) report.Add(name + "/StaticSamples", "Static arrangements are not motion examples.");
            for (int i = 0; i < m_Clips.Length; i++)
            {
                var clip = m_Clips[i];
                if (clip == null) { report.Add($"{name}/Clips[{i}]", "Null clip."); continue; }
                if (!DefinitionsAgree(m_Definition, clip.FrozenDefinition))
                    report.Add($"{name}/Clips[{i}]/Definition", "Frozen definition/settings must match the destination definition.");
                if (!MotionGestureRecognizer.TryCheckComplete(clip, out var result)) report.Add($"{name}/Clips[{i}]", result.Reason);
            }
            for (int i = 0; i < m_StaticSamples.Length; i++)
            {
                if (m_StaticSamples[i] == null) report.Add($"{name}/StaticSamples[{i}]", "Null sample.");
                else m_StaticSamples[i].Validate(m_Definition, $"{name}/StaticSamples[{i}]", report);
            }
            ValidateShape(m_DominantShape, m_Definition.DominantSide, name + "/DominantShape", report);
            ValidateShape(m_NonDominantShape, m_Definition.NonDominantSide, name + "/NonDominantShape", report);
            return report.IsValid;
        }
        static void ValidateShape(HandPoseData shape, HandSide side, string path, ValidationReport report)
        {
            if (shape == null) return;
            PoseDataValidation.ValidateTemplate(shape, path, report);
            if (shape.HandSide != side) report.Add(path, "Physical side does not match signing role.");
        }
        // At authoring/build time only. Include all fields, including provenance and tuning; no silent migration.
        public static bool DefinitionsAgree(GestureDefinition a, GestureDefinition b)
            => a != null && b != null && string.Equals(JsonUtility.ToJson(a), JsonUtility.ToJson(b), StringComparison.Ordinal);
    }
}
