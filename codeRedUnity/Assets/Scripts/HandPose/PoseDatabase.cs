using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    [CreateAssetMenu(fileName = "PoseDatabase", menuName = "Hand Pose/Database")]
    public sealed class PoseDatabase : ScriptableObject
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] HandPoseData[] m_Templates = new HandPoseData[0];
        public int SchemaVersion => m_SchemaVersion;
        public HandPoseData[] CopyTemplates() => m_Templates == null ? null : (HandPoseData[])m_Templates.Clone();

        public void SetTemplates(HandPoseData[] templates)
            => m_Templates = templates == null ? null : (HandPoseData[])templates.Clone();

        public bool TryBuildSnapshot(out PoseDatabaseSnapshot snapshot, out ValidationReport report)
        {
            snapshot = null;
            report = new ValidationReport();
            string path = $"Database[{name}]";
            if (m_SchemaVersion != 1) report.Add(path + "/SchemaVersion", "Only database schema 1 is supported.");
            if (m_Templates == null) { report.Add(path + "/Templates", "Null authoring array; use an empty array for an empty catalog."); return false; }
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < m_Templates.Length; i++)
            {
                var asset = m_Templates[i];
                string childPath = $"{path}/Templates[{i}]";
                if (asset != null) childPath += $"/Asset[{asset.name}]/Pose[{asset.PoseId}]";
                PoseDataValidation.ValidateTemplate(asset, childPath, report);
                if (asset != null && !string.IsNullOrEmpty(asset.PoseId))
                {
                    if (ids.TryGetValue(asset.PoseId, out var prior))
                        report.Add(childPath + "/PoseId", $"Duplicate ordinal ID '{asset.PoseId}' also used at {prior}.");
                    else ids.Add(asset.PoseId, childPath);
                }
            }
            if (!report.IsValid) return false;
            var templates = new PoseTemplate[m_Templates.Length];
            for (int i = 0; i < templates.Length; i++) templates[i] = new PoseTemplate(m_Templates[i]);
            snapshot = new PoseDatabaseSnapshot(templates);
            return true;
        }
    }
}
