using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition
{
    /// <summary>
    /// Reads one hand from Unity's XR Hands subsystem and converts it into the
    /// owned <see cref="HandFrame"/> plus the compatible <see cref="HandJointData"/> the classifier consumes. This
    /// is the only script that touches the XR Hands API; everything downstream
    /// is plain C#.
    /// </summary>
    /// <remarks>
    /// A MonoBehaviour is a component attached to a GameObject in the scene.
    /// Unity calls its methods (Update, OnDisable, ...) automatically; we never
    /// call them ourselves.
    /// </remarks>
    public class QuestHandAdapter : MonoBehaviour, IHandFrameSource
    {
        public enum TrackingState
        {
            Disabled,
            WaitingForSubsystem,
            WaitingForUpdate,
            NotTracked,
            IncompleteJoints,
            Tracked,
        }

        // [SerializeField] shows a private field in the Inspector and saves it
        // with the scene, without making it public to other scripts.
        [SerializeField, Tooltip("Which hand to read. v0.1 classifies one hand.")]
        Handedness m_Handedness = Handedness.Right;

        // Joint positions arrive in tracking space: local to the XR Origin's
        // Camera Offset, the tracked camera's parent. Only this transform's
        // world rotation is copied, so the thumb direction is converted to
        // world space exactly once. Assumes positive, uniform rig scale.
        [SerializeField, Tooltip("XR Origin's Camera Offset: the space of tracked joint positions. Its world rotation lets the classifier find world up for thumbs-up. Leave empty to disable orientation.")]
        Transform m_TrackingSpace;

        // Reused every search so finding the subsystem doesn't allocate garbage.
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        XRHandSubsystem m_Subsystem;
        HandJointData m_CurrentHand;
        bool m_HasReceivedUpdate;
        readonly JointPose[] m_CaptureScratch = HandFrame.CreateUnavailableJoints();
        HandFrame m_LatestFrame;
        ulong m_SequenceId;

        // Explicit provider mapping: project slot numbers are never cast to XR IDs.
        static readonly XRHandJointID[] s_ProviderIds =
        {
            XRHandJointID.Wrist, XRHandJointID.Palm,
            XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
            XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
            XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
            XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
            XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip
        };

        public HandSide HandSide => m_Handedness == Handedness.Left ? HandSide.Left
            : m_Handedness == Handedness.Right ? HandSide.Right : HandSide.Unknown;

        public bool TryGetLatestFrame(out HandFrame frame)
        {
            InvalidateStoppedSource();
            frame = m_LatestFrame;
            return frame != null;
        }

        void InvalidateStoppedSource()
        {
            if (m_LatestFrame != null && (m_LatestFrame.HandSide != HandSide
                || (m_LatestFrame.IsTracked && (!isActiveAndEnabled || m_Subsystem == null || !m_Subsystem.running))))
            {
                m_CurrentHand = default;
                PublishUntracked();
            }
        }

        void PublishUntracked()
        {
            for (int i = 0; i < m_CaptureScratch.Length; i++)
                m_CaptureScratch[i] = JointPose.Unavailable((HandJointId)i);
            PublishFrame(false);
        }

        void PublishFrame(bool tracked)
        {
            // Unity's monotonic real-time clock, sampled at acquisition/status publication.
            // Not wall time or a hardware sample timestamp. Sequence scope is this component lifetime.
            m_LatestFrame = new HandFrame(Time.realtimeSinceStartupAsDouble, checked(++m_SequenceId), HandSide,
                tracked, HandFrameSpace.TrackingSpace, m_CaptureScratch,
                trackingSpaceToWorld: m_CurrentHand.trackingSpaceToWorld,
                hasTrackingSpaceToWorld: m_CurrentHand.hasTrackingSpaceToWorld);
        }

        /// <summary>Which hand this adapter reads.</summary>
        public Handedness handedness => m_Handedness;

        /// <summary>
        /// The latest snapshot of the selected hand. <c>isTracked</c> is false
        /// whenever there is no running hand subsystem, the hand isn't tracked,
        /// or this component is disabled.
        /// </summary>
        public HandJointData currentHand { get { InvalidateStoppedSource(); return m_CurrentHand; } }

        /// <summary>Distinguishes missing tracking from an unrecognized pose.</summary>
        public TrackingState trackingState
        {
            get
            {
                if (!isActiveAndEnabled)
                    return TrackingState.Disabled;
                if (m_Subsystem == null || !m_Subsystem.running)
                    return TrackingState.WaitingForSubsystem;
                if (!m_HasReceivedUpdate)
                    return TrackingState.WaitingForUpdate;
                if (!m_CurrentHand.isTracked)
                    return TrackingState.NotTracked;
                if (!m_CurrentHand.index.isValid || !m_CurrentHand.middle.isValid
                    || !m_CurrentHand.ring.isValid || !m_CurrentHand.little.isValid)
                    return TrackingState.IncompleteJoints;
                return TrackingState.Tracked;
            }
        }

        void Update()
        {
            if (m_Subsystem != null && m_Subsystem.running)
                return;

            // No running subsystem (not started yet, or XR shut down): drop any
            // stale data so it can't be classified as a gesture, then look again.
            // The subsystem often doesn't exist yet on the first frames, which
            // is why we search in Update rather than once in Start.
            InvalidateStoppedSource();
            Unsubscribe();
            m_CurrentHand = default;

            // SubsystemManager is Unity's registry of active subsystems (XR
            // plug-ins register theirs there when XR starts).
            SubsystemManager.GetSubsystems(s_Subsystems);
            for (var i = 0; i < s_Subsystems.Count; ++i)
            {
                if (s_Subsystems[i].running)
                {
                    Subscribe(s_Subsystems[i]);
                    break;
                }
            }
        }

        void OnDisable()
        {
            Unsubscribe();
            m_CurrentHand = default;
            PublishUntracked();
        }

        void Subscribe(XRHandSubsystem subsystem)
        {
            m_Subsystem = subsystem;
            m_HasReceivedUpdate = false;
            // += adds our method to the subsystem's callback list; it then runs
            // every time the subsystem refreshes hand data.
            m_Subsystem.updatedHands += OnUpdatedHands;
        }

        void Unsubscribe()
        {
            m_HasReceivedUpdate = false;
            if (m_Subsystem == null)
                return;

            // Always remove callbacks we added, or the subsystem would keep
            // calling into this component after it is disabled or destroyed.
            m_Subsystem.updatedHands -= OnUpdatedHands;
            m_Subsystem = null;
        }

        void OnUpdatedHands(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            // The subsystem updates twice per frame. Dynamic runs near Update and
            // is the one meant for game logic; BeforeRender is for visuals.
            if (updateType != XRHandSubsystem.UpdateType.Dynamic)
                return;

            if (!isActiveAndEnabled) return;
            m_HasReceivedUpdate = true;
            var required = m_Handedness == Handedness.Left
                ? XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose | XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints
                : XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose | XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;
            bool selected = HandSide != HandSide.Unknown;
            var hand = m_Handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            bool tracked = selected && (updateSuccessFlags & required) == required && hand.isTracked;
            for (int i = 0; i < m_CaptureScratch.Length; i++)
            {
                var joint = JointPose.Unavailable((HandJointId)i);
                if (tracked && hand.GetJoint(s_ProviderIds[i]).TryGetPose(out var pose))
                    joint = new JointPose((HandJointId)i, pose.position, pose.rotation, true, true).Sanitized(true);
                m_CaptureScratch[i] = joint;
            }
            m_CurrentHand = ApplyTrackingSpace(HandFrameLegacyProjection.Project(m_CaptureScratch, tracked), m_TrackingSpace);
            PublishFrame(tracked);
        }

        /// <summary>
        /// Returns a copy of <paramref name="hand"/> carrying the tracking
        /// space's world rotation, or with orientation explicitly marked
        /// unavailable when <paramref name="trackingSpace"/> is null or destroyed.
        /// </summary>
        /// <remarks>
        /// Joint positions are left in tracking space; nothing else is
        /// transformed. <c>Transform.rotation</c> is the world rotation, which
        /// already includes every parent (the XR Origin), so no further
        /// rotation should be applied on top. Rotation-only conversion assumes
        /// the rig has positive, uniform scale; nonuniform or mirrored scaling
        /// is not supported in this version.
        /// </remarks>
        public static HandJointData ApplyTrackingSpace(HandJointData hand, Transform trackingSpace)
        {
            // Unity overrides == null, so a destroyed Transform also counts as missing.
            if (trackingSpace == null)
            {
                hand.trackingSpaceToWorld = default;
                hand.hasTrackingSpaceToWorld = false;
                return hand;
            }

            hand.trackingSpaceToWorld = trackingSpace.rotation;
            hand.hasTrackingSpaceToWorld = true;
            return hand;
        }
    }
}
