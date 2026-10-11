using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HandPoseRecognition
{
    public sealed class PoseDatabaseSnapshot
    {
        readonly Dictionary<string, PoseTemplate> m_ById = new Dictionary<string, PoseTemplate>(StringComparer.Ordinal);
        readonly ReadOnlyCollection<PoseTemplate> m_Left;
        readonly ReadOnlyCollection<PoseTemplate> m_Right;
        static readonly ReadOnlyCollection<PoseTemplate> s_Empty = Array.AsReadOnly(new PoseTemplate[0]);
        public IReadOnlyList<PoseTemplate> Templates { get; }
        public int PoseCount => Templates.Count;
        public int SampleCount { get; }
        public int SchemaVersion => 1;

        internal PoseDatabaseSnapshot(PoseTemplate[] templates)
        {
            var owned = (PoseTemplate[])templates.Clone();
            Array.Sort(owned, (a, b) => StringComparer.Ordinal.Compare(a.PoseId, b.PoseId));
            Templates = Array.AsReadOnly(owned);
            var left = new List<PoseTemplate>();
            var right = new List<PoseTemplate>();
            int samples = 0;
            foreach (var template in owned)
            {
                m_ById.Add(template.PoseId, template);
                (template.HandSide == HandSide.Left ? left : right).Add(template);
                samples += template.Samples.Count;
            }
            SampleCount = samples;
            m_Left = left.AsReadOnly();
            m_Right = right.AsReadOnly();
        }

        public bool TryGetPose(string poseId, out PoseTemplate template)
        {
            template = null;
            return poseId != null && m_ById.TryGetValue(poseId, out template);
        }

        public IReadOnlyList<PoseTemplate> GetCandidates(HandSide side)
            => side == HandSide.Left ? m_Left : side == HandSide.Right ? m_Right : s_Empty;
    }
}
