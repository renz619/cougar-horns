using System;
using System.Collections.Generic;

namespace HandPoseRecognition
{
    public sealed class GestureTemplate
    {
        readonly GestureDefinition m_Definition;
        public GestureDefinition Definition => m_Definition.Copy();
        internal GestureDefinition FrozenDefinition => m_Definition;
        public string GestureId => m_Definition.GestureId;
        public bool IsSupported => m_Definition.IsSupported;
        public IReadOnlyList<GestureClip> Clips { get; }
        public IReadOnlyList<GestureStaticSample> StaticSamples { get; }
        public PoseTemplate DominantShape { get; }
        public PoseTemplate NonDominantShape { get; }
        internal readonly GestureFeatureSequence[] Features;
        internal GestureTemplate(GestureData data)
        {
            m_Definition = data.Definition;
            var clips = data.CopyClips(); Clips = Array.AsReadOnly(clips);
            StaticSamples = Array.AsReadOnly(data.CopyStaticSamples());
            DominantShape = data.DominantShape == null ? null : new PoseTemplate(data.DominantShape);
            NonDominantShape = data.NonDominantShape == null ? null : new PoseTemplate(data.NonDominantShape);
            Features = new GestureFeatureSequence[clips.Length];
            for (int i = 0; i < clips.Length; i++)
                if (!GestureFeatureExtractor.TryExtract(clips[i], out Features[i], out var reason)) throw new InvalidOperationException(reason);
        }
    }

    /// <summary>Atomic owned snapshot; examples are preprocessed once on explicit build, never in each DTW comparison.</summary>
    public sealed class GestureCatalogSnapshot
    {
        readonly Dictionary<string, GestureTemplate> m_ById = new Dictionary<string, GestureTemplate>(StringComparer.Ordinal);
        public IReadOnlyList<GestureTemplate> Entries { get; }
        internal GestureCatalogSnapshot(GestureData[] assets)
        {
            var entries = new GestureTemplate[assets.Length];
            for (int i = 0; i < entries.Length; i++) { entries[i] = new GestureTemplate(assets[i]); m_ById.Add(entries[i].GestureId, entries[i]); }
            Array.Sort(entries, (a, b) => string.CompareOrdinal(a.GestureId, b.GestureId)); Entries = Array.AsReadOnly(entries);
        }
        public bool TryGetGesture(string id, out GestureTemplate template)
        { template = null; return id != null && m_ById.TryGetValue(id, out template); }
    }
}
