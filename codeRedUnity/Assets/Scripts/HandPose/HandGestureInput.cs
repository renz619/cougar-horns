using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Shared integration helper, not a new tracking system. Subscribes to every Dynamic publication and honors source availability.</summary>
    internal sealed class HandGestureInput : IDisposable
    {
        MonoBehaviour m_Source;
        IHandFrameStream m_Stream;
        GestureReferenceProvider m_Reference;
        GestureObservation m_Latest;
        readonly Action<GestureObservation> m_OnAcquisition;
        ulong m_Epoch, m_BlockedSequence;
        bool m_BlockOldSequence;
        public string SourceIdentity { get; private set; } = Guid.NewGuid().ToString("N");
        public HandGestureInput(Action<GestureObservation> onAcquisition) { m_OnAcquisition = onAcquisition; }
        public void Configure(MonoBehaviour source, GestureReferenceProvider reference)
        {
            if (!ReferenceEquals(source, m_Source))
            {
                if (m_Stream != null) m_Stream.FramePublished -= Published;
                m_Source = source; m_Latest = null; m_BlockOldSequence = false; SourceIdentity = Guid.NewGuid().ToString("N");
                m_Stream = source as IHandFrameStream;
                if (m_Stream != null) m_Stream.FramePublished += Published;
            }
            if (m_Reference != reference) { m_Reference = reference; BlockOldCoordinates(); }
            ulong epoch = m_Reference == null ? 0 : m_Reference.CoordinateEpoch;
            if (m_Epoch != epoch) { BlockOldCoordinates(); m_Epoch = epoch; }
        }
        void BlockOldCoordinates()
        { if (m_Latest != null) { m_BlockedSequence = m_Latest.AcquisitionId; m_BlockOldSequence = true; } m_Latest = null; }
        void Published(HandFrame frame)
        {
            Configure(m_Source, m_Reference);
            if (m_Source == null || !m_Source.isActiveAndEnabled) { m_Latest = null; m_OnAcquisition(null); return; }
            m_OnAcquisition(Observe(frame));
        }
        GestureObservation Observe(HandFrame frame)
        {
            if (frame == null || m_BlockOldSequence && frame.SequenceId <= m_BlockedSequence) return null;
            m_BlockOldSequence = false;
            if (m_Latest != null && m_Latest.AcquisitionId == frame.SequenceId) return m_Latest;
            m_Latest = new GestureObservation(frame.TimestampSeconds, frame.SequenceId, SourceIdentity, m_Epoch,
                left: frame.HandSide == HandSide.Left ? frame : null, right: frame.HandSide == HandSide.Right ? frame : null);
            return m_Latest;
        }
        public GestureObservation Read()
        {
            Configure(m_Source, m_Reference);
            if (m_Source == null || !m_Source.isActiveAndEnabled || !(m_Source is IHandFrameSource source)
                || !source.TryGetLatestFrame(out var frame)) return null;
            return Observe(frame);
        }
        public void Dispose()
        { if (m_Stream != null) m_Stream.FramePublished -= Published; m_Stream = null; m_Source = null; m_Latest = null; }
    }
}
