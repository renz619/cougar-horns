using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace HandPoseRecognition
{
    /// <summary>
    /// Reads one hand from Unity's XR Hands subsystem and converts it into the
    /// plain <see cref="HandJointData"/> snapshot the classifier consumes. This
    /// is the only script that touches the XR Hands API; everything downstream
    /// is plain C#.
    /// </summary>
    /// <remarks>
    /// A MonoBehaviour is a component attached to a GameObject in the scene.
    /// Unity calls its methods (Update, OnDisable, ...) automatically; we never
    /// call them ourselves.
    /// </remarks>
    public class QuestHandAdapter : MonoBehaviour
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

        /// <summary>Which hand this adapter reads.</summary>
        public Handedness handedness => m_Handedness;

        /// <summary>
        /// The latest snapshot of the selected hand. <c>isTracked</c> is false
        /// whenever there is no running hand subsystem, the hand isn't tracked,
        /// or this component is disabled.
        /// </summary>
        public HandJointData currentHand => m_CurrentHand;

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

            m_HasReceivedUpdate = true;

            switch (m_Handedness)
            {
                case Handedness.Left:
                    m_CurrentHand = ReadHand(subsystem.leftHand);
                    break;
                case Handedness.Right:
                    m_CurrentHand = ReadHand(subsystem.rightHand);
                    break;
                default:
                    m_CurrentHand = default;
                    break;
            }

            m_CurrentHand = ApplyTrackingSpace(m_CurrentHand, m_TrackingSpace);
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

        static HandJointData ReadHand(XRHand hand)
        {
            // XRHand.isTracked is recomputed on every subsystem update from
            // whether both the root pose and the joints were updated.
            if (!hand.isTracked)
                return default;

            return new HandJointData
            {
                isTracked = true,
                thumb = ReadThumb(hand),
                index = ReadFinger(hand,
                    XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate,
                    XRHandJointID.IndexDistal, XRHandJointID.IndexTip),
                middle = ReadFinger(hand,
                    XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate,
                    XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip),
                ring = ReadFinger(hand,
                    XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate,
                    XRHandJointID.RingDistal, XRHandJointID.RingTip),
                little = ReadFinger(hand,
                    XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate,
                    XRHandJointID.LittleDistal, XRHandJointID.LittleTip),
            };
        }

        static FingerJoints ReadFinger(
            XRHand hand,
            XRHandJointID metacarpalId, XRHandJointID proximalId, XRHandJointID intermediateId,
            XRHandJointID distalId, XRHandJointID tipId)
        {
            // TryGetPose returns false when the runtime didn't supply this
            // joint's pose. Poses are relative to the XR Origin, not world space;
            // that's fine because the classifier only measures angles between
            // bones. The & operator (not &&) evaluates every call so all five
            // positions are always filled in.
            var valid = TryGetPosition(hand, metacarpalId, out var metacarpal)
                & TryGetPosition(hand, proximalId, out var proximal)
                & TryGetPosition(hand, intermediateId, out var intermediate)
                & TryGetPosition(hand, distalId, out var distal)
                & TryGetPosition(hand, tipId, out var tip);

            return new FingerJoints
            {
                metacarpal = metacarpal,
                proximal = proximal,
                intermediate = intermediate,
                distal = distal,
                tip = tip,
                isValid = valid,
            };
        }

        static ThumbJoints ReadThumb(XRHand hand)
        {
            // Same pattern as ReadFinger. The thumb has only four joints: XR
            // Hands defines no ThumbIntermediate, because the thumb has one bone
            // fewer than the other fingers.
            var valid = TryGetPosition(hand, XRHandJointID.ThumbMetacarpal, out var metacarpal)
                & TryGetPosition(hand, XRHandJointID.ThumbProximal, out var proximal)
                & TryGetPosition(hand, XRHandJointID.ThumbDistal, out var distal)
                & TryGetPosition(hand, XRHandJointID.ThumbTip, out var tip);

            return new ThumbJoints
            {
                metacarpal = metacarpal,
                proximal = proximal,
                distal = distal,
                tip = tip,
                isValid = valid,
            };
        }

        static bool TryGetPosition(XRHand hand, XRHandJointID id, out Vector3 position)
        {
            var found = hand.GetJoint(id).TryGetPose(out var pose);
            position = pose.position;
            return found;
        }
    }
}
