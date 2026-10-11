using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    [CreateAssetMenu(menuName = "Hand Gesture/Catalog")]
    public sealed class GestureCatalog : ScriptableObject
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] int m_MaxEntries = 64, m_MaxTotalClips = 256;
        [SerializeField] GestureData[] m_Entries = Array.Empty<GestureData>();
        public int SchemaVersion => m_SchemaVersion;
        public int MaxEntries => m_MaxEntries;
        public int MaxTotalClips => m_MaxTotalClips;
        public GestureData[] CopyEntries() => m_Entries == null ? null : (GestureData[])m_Entries.Clone();
        public void SetEntries(GestureData[] entries, int maxEntries = 64, int maxTotalClips = 256)
        { m_Entries = entries == null ? null : (GestureData[])entries.Clone(); m_MaxEntries = maxEntries; m_MaxTotalClips = maxTotalClips; }
        public bool TryBuildSnapshot(out GestureCatalogSnapshot snapshot, out ValidationReport report)
        {
            snapshot = null; report = new ValidationReport();
            if (m_SchemaVersion != 1) report.Add(name + "/SchemaVersion", "Only gesture catalog schema 1 is supported.");
            if (m_MaxEntries < 1 || m_MaxEntries > 256 || m_MaxTotalClips < 1 || m_MaxTotalClips > 1024)
                report.Add(name + "/Budgets", "Entry cap must be 1..256; total clip cap 1..1024.");
            if (m_Entries == null || m_Entries.Length > m_MaxEntries) { report.Add(name + "/Entries", "Missing or over-budget reference array."); return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            for (int i = 0; i < m_Entries.Length; i++)
            {
                var entry = m_Entries[i];
                if (entry == null) { report.Add($"{name}/Entries[{i}]", "Null reference."); continue; }
                if (!entry.TryValidate(out var child)) foreach (var error in child.Errors) report.Add($"{name}/Entries[{i}]", error);
                if (entry.FrozenDefinition != null && !ids.Add(entry.FrozenDefinition.GestureId)) report.Add($"{name}/Entries[{i}]", "Duplicate ordinal gesture ID.");
                total += entry.Clips?.Length ?? 0;
            }
            if (total > m_MaxTotalClips) report.Add(name + "/Clips", "Total clip budget exceeded.");
            if (!report.IsValid) return false;
            snapshot = new GestureCatalogSnapshot(m_Entries); return true;
        }
    }
}
