using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Serialized raw evidence is distinct from the unchanged static canonical schema.</summary>
    [Serializable]
    sealed class GestureRawHand
    {
        [SerializeField] double m_Time;
        [SerializeField] ulong m_Sequence;
        [SerializeField] HandSide m_Side;
        [SerializeField] bool m_Tracked;
        [SerializeField] HandFrameSpace m_Space;
        [SerializeField] int m_Layout, m_Normalization;
        [SerializeField] JointPose[] m_Joints;
        [SerializeField] Quaternion m_TrackingRotation;
        [SerializeField] bool m_HasTrackingRotation;
        [NonSerialized] HandFrame m_Frame;
        internal GestureRawHand(HandFrame frame)
        {
            m_Time = frame.TimestampSeconds; m_Sequence = frame.SequenceId; m_Side = frame.HandSide;
            m_Tracked = frame.IsTracked; m_Space = frame.Space; m_Layout = frame.JointLayoutVersion;
            m_Normalization = frame.NormalizationVersion; m_Joints = frame.CopyJoints();
            m_TrackingRotation = frame.TrackingSpaceToWorld; m_HasTrackingRotation = frame.HasTrackingSpaceToWorld;
            m_Frame = frame;
        }
        internal HandFrame Frame
        {
            get
            {
                if (m_Frame == null) m_Frame = new HandFrame(m_Time, m_Sequence, m_Side, m_Tracked, m_Space,
                    m_Joints, m_Layout, m_Normalization, m_TrackingRotation, m_HasTrackingRotation);
                return m_Frame;
            }
        }
    }

    /// <summary>Optional physical slots; acquisition ID is producer-wide, hand sequences remain source-local.</summary>
    [Serializable]
    public sealed class GestureObservation
    {
        [SerializeField] int m_SchemaVersion = 1;
        [SerializeField] double m_TimestampSeconds;
        [SerializeField] ulong m_AcquisitionId, m_CoordinateEpoch;
        [SerializeField] string m_SourceIdentity;
        [SerializeField] bool m_HasLeft, m_HasRight;
        [SerializeField] GestureRawHand m_Left, m_Right;
        public int SchemaVersion => m_SchemaVersion;
        public double TimestampSeconds => m_TimestampSeconds;
        public ulong AcquisitionId => m_AcquisitionId;
        public string SourceIdentity => m_SourceIdentity;
        public ulong CoordinateEpoch => m_CoordinateEpoch;
        // Unity serializes inline classes by value and may instantiate an absent slot on reload.
        // Presence flags keep that placeholder distinct from actual observed hand evidence.
        public HandFrame Left => m_HasLeft ? m_Left?.Frame : null;
        public HandFrame Right => m_HasRight ? m_Right?.Frame : null;
        public GestureHandMask AvailableHands => (Left != null && Left.IsTracked ? GestureHandMask.Left : GestureHandMask.None)
            | (Right != null && Right.IsTracked ? GestureHandMask.Right : GestureHandMask.None);
        public GestureObservation(double timestamp, ulong acquisitionId, string sourceIdentity, ulong coordinateEpoch, HandFrame left = null, HandFrame right = null)
        {
            m_TimestampSeconds = timestamp; m_AcquisitionId = acquisitionId; m_SourceIdentity = sourceIdentity; m_CoordinateEpoch = coordinateEpoch;
            m_HasLeft = left != null; m_HasRight = right != null;
            m_Left = left == null ? null : new GestureRawHand(left); m_Right = right == null ? null : new GestureRawHand(right);
        }
        public HandFrame GetHand(HandSide side) => side == HandSide.Left ? Left : side == HandSide.Right ? Right : null;
        public GestureObservation Clone()
        { var copy = new GestureObservation(TimestampSeconds, AcquisitionId, SourceIdentity, CoordinateEpoch, Left, Right); copy.m_SchemaVersion = m_SchemaVersion; return copy; }
        public bool TryValidate(out string reason)
        {
            reason = null;
            if (m_SchemaVersion != 1 || !PoseMath.IsFinite(TimestampSeconds) || TimestampSeconds < 0 || string.IsNullOrWhiteSpace(SourceIdentity))
            { reason = "Observation needs schema 1, finite time and explicit source/session identity."; return false; }
            try
            {
                if (Left != null && Left.HandSide != HandSide.Left || Right != null && Right.HandSide != HandSide.Right)
                { reason = "Physical hand does not match its observation slot."; return false; }
                if (Left == null && Right == null) { reason = "Observation has no hand evidence."; return false; }
                if (Left != null && Left.TimestampSeconds > TimestampSeconds || Right != null && Right.TimestampSeconds > TimestampSeconds)
                { reason = "Hand evidence is ahead of the common observation timestamp."; return false; }
            }
            catch (ArgumentException error) { reason = "Malformed serialized raw evidence: " + error.Message; return false; }
            return true;
        }
    }

    /// <summary>Numerical calibration. Up/forward are expressed in the same tracking space as joint positions.</summary>
    [Serializable]
    public sealed class GestureCalibration
    {
        [SerializeField] int m_Version = 1;
        [SerializeField] Vector3 m_Up, m_Forward, m_RigScale;
        [SerializeField] ulong m_CoordinateEpoch;
        public int Version => m_Version;
        public Vector3 Up => m_Up;
        public Vector3 Forward => m_Forward;
        public Vector3 RigScale => m_RigScale;
        public ulong CoordinateEpoch => m_CoordinateEpoch;
        public GestureCalibration(Vector3 up, Vector3 forward, Vector3 rigScale, ulong coordinateEpoch)
        { m_Up = up; m_Forward = forward; m_RigScale = rigScale; m_CoordinateEpoch = coordinateEpoch; }
        public GestureCalibration(Quaternion basis, float uniformScale, ulong coordinateEpoch)
        {
            bool valid = PoseMath.TryNormalize(basis, out var normalized);
            m_Up = valid ? normalized * Vector3.up : Vector3.zero;
            m_Forward = valid ? normalized * Vector3.forward : Vector3.zero;
            m_RigScale = Vector3.one * uniformScale; m_CoordinateEpoch = coordinateEpoch;
        }
        public bool TryGetBasis(out Quaternion basis, out string reason)
        {
            basis = Quaternion.identity; reason = null;
            if (Version != 1 || !PoseMath.IsFinite(Up) || !PoseMath.IsFinite(Forward) || !PoseMath.IsFinite(RigScale)
                || RigScale.x <= 0 || RigScale.y <= 0 || RigScale.z <= 0
                || Math.Abs(RigScale.x - RigScale.y) > 1e-5 * RigScale.x || Math.Abs(RigScale.x - RigScale.z) > 1e-5 * RigScale.x)
            { reason = "Calibration requires version 1, finite axes and positive uniform rig scale."; return false; }
            if (!PoseMath.IsFinite(Up.magnitude) || Up.magnitude < 1e-5f)
            { reason = "Calibration up is degenerate."; return false; }
            var up = Up.normalized; var forward = Forward - Vector3.Dot(Forward, up) * up;
            if (!PoseMath.IsFinite(forward) || !PoseMath.IsFinite(forward.magnitude) || forward.magnitude < 1e-5f)
            { reason = "Calibration horizontal forward is degenerate."; return false; }
            if (!PoseMath.TryNormalize(Quaternion.LookRotation(forward.normalized, up), out basis))
            { reason = "Calibration basis is invalid."; return false; }
            return true;
        }
    }

    [Serializable]
    public sealed class GestureReference
    {
        [SerializeField] int m_FeatureVersion = 1, m_CalibrationVersion = 1;
        [SerializeField] ulong m_CoordinateEpoch;
        [SerializeField] string m_SourceIdentity;
        [SerializeField] Quaternion m_Basis;
        [SerializeField] float m_PalmWidth;
        [SerializeField] Vector3 m_InitialTip, m_InitialWrist, m_SharedOrigin;
        [SerializeField] HandSide m_DominantSide;
        public int FeatureVersion => m_FeatureVersion;
        public int CalibrationVersion => m_CalibrationVersion;
        public ulong CoordinateEpoch => m_CoordinateEpoch;
        public string SourceIdentity => m_SourceIdentity;
        public Quaternion Basis => m_Basis;
        public float PalmWidth => m_PalmWidth;
        public Vector3 InitialTip => m_InitialTip;
        public Vector3 InitialWrist => m_InitialWrist;
        public Vector3 SharedOrigin => m_SharedOrigin;
        public HandSide DominantSide => m_DominantSide;
        internal GestureReference(GestureObservation observation, HandSide dominantSide, Quaternion basis, float palmWidth, Vector3 initialTip, Vector3 initialWrist)
        {
            m_CoordinateEpoch = observation.CoordinateEpoch; m_SourceIdentity = observation.SourceIdentity;
            m_Basis = basis; m_PalmWidth = palmWidth; m_InitialTip = initialTip; m_InitialWrist = m_SharedOrigin = initialWrist;
            m_DominantSide = dominantSide;
        }
        public GestureReference Clone() => (GestureReference)MemberwiseClone();
        public bool TryValidate(out string reason)
        {
            reason = null;
            if (FeatureVersion != 1 || CalibrationVersion != 1 || !PoseDataValidation.KnownSide(DominantSide)
                || string.IsNullOrWhiteSpace(SourceIdentity) || !PoseMath.TryNormalize(Basis, out var normalized)
                || Math.Abs(Quaternion.Dot(Basis, normalized) - 1) > 1e-4
                || !PoseMath.IsFinite(PalmWidth) || PalmWidth < HandPoseNormalizer.MinimumPalmWidthMeters
                || !PoseMath.IsFinite(InitialTip) || !PoseMath.IsFinite(InitialWrist) || !PoseMath.IsFinite(SharedOrigin))
            { reason = "Invalid frozen reference metadata, basis, origin or palm-width scale."; return false; }
            return true;
        }
    }
}
