using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum GestureRecognitionState { Idle, Armed, Tracking, Completed, Rejected, AwaitRearm }

    /// <summary>Injected-clock J/Z-only arbitration. No static letter is emitted by this optional motion lesson path.</summary>
    public sealed class GestureRecognitionSession
    {
        readonly List<GestureObservation> m_Frames = new List<GestureObservation>();
        readonly List<GestureFeaturePoint> m_Trail = new List<GestureFeaturePoint>();
        readonly MotionGestureRecognizer m_Recognizer = new MotionGestureRecognizer();
        GestureCatalogSnapshot m_Catalog;
        GestureDefinition m_Definition;
        GestureReference m_Reference;
        GestureObservation m_Last;
        string m_Source;
        ulong m_Epoch;
        double m_LastNow = -1, m_Start, m_LastMotionTime, m_ReleaseStart = -1, m_LastEventTime = double.NegativeInfinity;
        int m_Attempts;
        Vector3 m_MovementAnchor;
        public GestureRecognitionState State { get; private set; }
        public GestureRecognitionResult LatestObservation { get; private set; }
        public string Reason { get; private set; } = "Motion lesson is idle.";
        public string ConfirmedGestureId { get; private set; }
        public string PendingGestureId => m_Definition?.GestureId;
        public double PhaseCoverage { get; private set; }
        public int FrameCount => m_Frames.Count;
        public double DurationSeconds => m_Frames.Count < 2 ? 0 : m_Frames[m_Frames.Count - 1].TimestampSeconds - m_Frames[0].TimestampSeconds;
        public GestureClip CurrentClip => m_Definition == null || m_Reference == null ? null : new GestureClip(m_Definition, m_Reference, m_Frames.ToArray());
        public IReadOnlyList<GestureFeaturePoint> Trail { get; }
        public IReadOnlyList<GestureObservation> Frames { get; }
        public HandJointId TrajectoryJoint => m_Definition?.TrajectoryJoint ?? HandJointId.Invalid;
        public event Action<GestureRecognitionResult> GestureConfirmed;
        public GestureRecognitionSession() { Trail = m_Trail.AsReadOnly(); Frames = m_Frames.AsReadOnly(); }

        /// <summary>Context changes and disable reset cooldown as well as evidence; an old clip can never be replayed.</summary>
        public void Reset(string reason = "Motion state reset.")
        {
            m_Frames.Clear(); m_Trail.Clear(); m_Definition = null; m_Reference = null; m_Last = null; m_Source = null;
            m_Catalog = null; m_LastNow = -1; m_ReleaseStart = -1; m_LastEventTime = double.NegativeInfinity; m_Attempts = 0;
            LatestObservation = default; ConfirmedGestureId = null; PhaseCoverage = 0; State = GestureRecognitionState.Idle; Reason = reason;
        }

        public void Update(GestureObservation observation, GestureCalibration calibration, GestureCatalogSnapshot catalog, double now)
        {
            if (!PoseMath.IsFinite(now) || now < 0 || m_LastNow >= 0 && now < m_LastNow)
            { Reset("Invalid or backwards motion clock."); return; }
            if (!ReferenceEquals(m_Catalog, catalog)) { Reset("Gesture catalog replaced."); m_Catalog = catalog; }
            m_LastNow = now;
            if (catalog == null) { ClearUnavailable("Gesture catalog unavailable."); return; }
            if (calibration == null) { ClearUnavailable("Reference calibration unavailable."); return; }
            if (!calibration.TryGetBasis(out _, out var calibrationReason)) { ClearUnavailable(calibrationReason); return; }
            if (m_Source != null && (calibration.CoordinateEpoch != m_Epoch
                || observation != null && (observation.SourceIdentity != m_Source || observation.CoordinateEpoch != m_Epoch)))
            {
                Reset("Source or coordinate epoch changed; start a new performance."); m_Catalog = catalog; m_LastNow = now;
                // A context discontinuity never continues the current suffix in this update.
                return;
            }
            if (State == GestureRecognitionState.Completed || State == GestureRecognitionState.Rejected) State = GestureRecognitionState.AwaitRearm;
            if (observation == null) { Lose("Hand tracking unavailable."); return; }
            if (!observation.TryValidate(out var observationReason)) { Lose(observationReason); return; }
            if (observation.CoordinateEpoch != calibration.CoordinateEpoch) { Lose("Acquisition and calibration epochs differ."); return; }
            if (m_Definition == null)
            {
                foreach (var candidate in catalog.Entries)
                {
                    var definition = candidate.FrozenDefinition;
                    if (!candidate.IsSupported || !Fresh(observation, definition, now, out _)
                        || !MotionGestureRecognizer.MatchesShape(observation.GetHand(definition.DominantSide), definition, false, out _)) continue;
                    if (!GestureFeatureExtractor.TryFreezeReference(observation, definition, calibration, out m_Reference, out var reason))
                    { Reason = reason; continue; }
                    m_Definition = definition; m_Source = observation.SourceIdentity; m_Epoch = observation.CoordinateEpoch;
                    m_Start = now; m_Last = null; m_Attempts = 0; m_Frames.Clear(); m_Trail.Clear(); PhaseCoverage = 0;
                    State = GestureRecognitionState.Armed; Reason = "Ready; begin the complete movement."; break;
                }
                if (m_Definition == null) { Reason = "Waiting for a supported right-hand J/Z starting shape."; return; }
            }
            var settings = m_Definition.Settings;
            if (!Fresh(observation, m_Definition, now, out var freshReason)) { Lose(freshReason); return; }
            var hand = observation.GetHand(m_Definition.DominantSide);
            if (m_Last != null && (observation.AcquisitionId < m_Last.AcquisitionId || hand.SequenceId < m_Last.GetHand(m_Definition.DominantSide).SequenceId))
            { Lose("Acquisition sequence moved backwards."); return; }
            bool duplicate = m_Last != null && (observation.AcquisitionId == m_Last.AcquisitionId || hand.SequenceId == m_Last.GetHand(m_Definition.DominantSide).SequenceId);
            if (m_Last != null && now - m_Last.TimestampSeconds > settings.MaxGapSeconds && (duplicate || observation.TimestampSeconds - m_Last.TimestampSeconds > settings.MaxGapSeconds))
            { Lose("Hand tracking gap; start a new complete movement."); return; }
            if (State == GestureRecognitionState.AwaitRearm)
            {
                // Distinct, valid non-gate acquisitions must span release. Cooldown alone cannot rearm.
                if (duplicate) return;
                if (hand.TimestampSeconds != observation.TimestampSeconds
                    || m_Last != null && observation.TimestampSeconds <= m_Last.TimestampSeconds)
                { Lose("Release acquisition timestamps must agree and strictly advance."); return; }
                if (!GestureFeatureExtractor.TryValidateHand(hand, m_Definition, out var releaseReason)) { Lose(releaseReason); return; }
                bool anyStartGate = false;
                foreach (var candidate in catalog.Entries)
                    if (candidate.IsSupported && MotionGestureRecognizer.MatchesShape(hand, candidate.FrozenDefinition, false, out _)) { anyStartGate = true; break; }
                m_Last = observation;
                if (anyStartGate) { m_ReleaseStart = -1; return; }
                ConfirmedGestureId = null;
                if (m_ReleaseStart < 0) m_ReleaseStart = observation.TimestampSeconds;
                if (observation.TimestampSeconds - m_ReleaseStart >= settings.RearmSeconds && now - m_LastEventTime >= settings.CooldownSeconds)
                {
                    m_Definition = null; m_Reference = null; m_Frames.Clear(); m_Trail.Clear(); m_Last = null;
                    LatestObservation = default; PhaseCoverage = 0; State = GestureRecognitionState.Idle; Reason = "Released; ready for a new performance.";
                }
                return;
            }
            if (now - m_Start >= settings.MaxDurationSeconds) { Reject("Motion timed out before explicit completion."); return; }
            if (duplicate) return;
            m_Attempts++;
            if (!GestureFeatureExtractor.TryValidateObservation(observation, m_Definition, m_Reference, m_Last, out var evidenceReason))
            { Lose(evidenceReason); return; }
            bool finish = DurationSeconds >= settings.MinDurationSeconds && PhaseCoverage >= .5;
            if (!MotionGestureRecognizer.MatchesShape(hand, m_Definition, finish, out var gateReason)) { Reject(gateReason); m_Last = observation; return; }
            m_Last = observation;
            m_Frames.Add(observation);
            if (State == GestureRecognitionState.Armed)
            {
                hand.TryGetJoint(m_Definition.TrajectoryJoint, out var tip);
                double displacement = GestureFeatureExtractor.Distance(tip.Position, m_Reference.InitialTip) / m_Reference.PalmWidth;
                if (displacement >= settings.StartMovementThreshold) { State = GestureRecognitionState.Tracking; Reason = "Follow the whole movement."; }
                else
                {
                    while (m_Frames.Count > settings.PreMovementFrames || m_Frames.Count > 2 && observation.TimestampSeconds - m_Frames[0].TimestampSeconds > settings.PreMovementSeconds)
                        m_Frames.RemoveAt(0);
                }
            }
            UpdateTrail();
            var lastPoint = m_Trail[m_Trail.Count - 1];
            if (m_Trail.Count == 1 || GestureFeatureExtractor.Distance(lastPoint.Tip, m_MovementAnchor) >= settings.PauseMovementThreshold)
            { m_MovementAnchor = lastPoint.Tip; m_LastMotionTime = observation.TimestampSeconds; }
            bool phases = MotionGestureRecognizer.CheckPhases(m_Trail, m_Definition, out var coverage, out var phaseReason);
            PhaseCoverage = coverage;
            if (State == GestureRecognitionState.Tracking) Reason = phaseReason;
            if (State == GestureRecognitionState.Tracking && phases && observation.TimestampSeconds - m_LastMotionTime >= settings.EndPauseSeconds)
            {
                LatestObservation = m_Recognizer.Recognize(CurrentClip, catalog);
                if (LatestObservation.IsMatch)
                {
                    State = GestureRecognitionState.Completed; ConfirmedGestureId = LatestObservation.GestureId; Reason = "Complete motion confirmed. Release before repeating.";
                    m_LastEventTime = now; m_ReleaseStart = -1;
                    GestureConfirmed?.Invoke(LatestObservation); return;
                }
                Reject(LatestObservation.Reason, false); return;
            }
            if (m_Frames.Count >= settings.MaxFrames || m_Attempts >= settings.MaxAttempts)
                Reject("Motion buffer/attempt budget reached; a truncated trail is not completion.");
        }

        static bool Fresh(GestureObservation observation, GestureDefinition definition, double now, out string reason)
        {
            var hand = observation.GetHand(definition.DominantSide); reason = null;
            if (hand == null || !hand.IsTracked || hand.TimestampSeconds > now || now - hand.TimestampSeconds > definition.Settings.MaxFrameAgeSeconds
                || observation.TimestampSeconds > now || now - observation.TimestampSeconds > definition.Settings.MaxFrameAgeSeconds)
            { reason = "Hand tracking lost, stale or ahead of the session clock."; return false; }
            return true;
        }
        void UpdateTrail()
        {
            // No frame/sample copies: display points are rebuilt in a capacity-reusing bounded list.
            m_Trail.Clear();
            var first = m_Frames[0].GetHand(m_Definition.DominantSide); first.TryGetJoint(m_Definition.TrajectoryJoint, out var startTip);
            first.TryGetJoint(HandJointId.Wrist, out var startWrist); var inverse = Quaternion.Inverse(m_Reference.Basis);
            foreach (var observation in m_Frames)
            {
                var frame = observation.GetHand(m_Definition.DominantSide); frame.TryGetJoint(m_Definition.TrajectoryJoint, out var tip); frame.TryGetJoint(HandJointId.Wrist, out var wrist);
                m_Trail.Add(new GestureFeaturePoint(observation.TimestampSeconds - m_Frames[0].TimestampSeconds,
                    inverse * ((tip.Position - startTip.Position) / m_Reference.PalmWidth), inverse * ((wrist.Position - startWrist.Position) / m_Reference.PalmWidth), inverse * wrist.Rotation));
            }
        }
        void Reject(string reason, bool clearObservation = true)
        {
            ConfirmedGestureId = null; State = GestureRecognitionState.Rejected; Reason = reason; m_ReleaseStart = -1;
            if (clearObservation) LatestObservation = new GestureRecognitionResult(GestureRecognitionStatus.Incomplete, CurrentClip, reason, PhaseCoverage);
        }
        void Lose(string reason)
        {
            ConfirmedGestureId = null; LatestObservation = default; m_ReleaseStart = -1; m_Last = null;
            if (m_Definition != null) State = GestureRecognitionState.AwaitRearm;
            Reason = reason;
        }
        void ClearUnavailable(string reason)
        { if (m_Definition == null) { ConfirmedGestureId = null; LatestObservation = default; Reason = reason; } else Lose(reason); }
    }
}
