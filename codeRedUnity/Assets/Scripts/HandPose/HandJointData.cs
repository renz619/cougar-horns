using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Joint positions for one non-thumb finger, ordered from the knuckle base
    /// outward. These map to the XR Hands joints
    /// XRHandJointID.{Finger}Metacarpal, Proximal, Intermediate, Distal and Tip.
    /// </summary>
    /// <remarks>
    /// This is a struct (a value type), so copying it copies the data. Passing
    /// it around never shares or mutates someone else's copy, and it creates no
    /// garbage for the collector each frame.
    /// </remarks>
    public struct FingerJoints
    {
        public Vector3 metacarpal;
        public Vector3 proximal;
        public Vector3 intermediate;
        public Vector3 distal;
        public Vector3 tip;

        /// <summary>
        /// False if any of this finger's joint poses could not be read
        /// (XRHandJoint.TryGetPose returned false). Defaults to false, so
        /// unfilled data is treated as invalid.
        /// </summary>
        public bool isValid;
    }

    /// <summary>
    /// Joint positions for the thumb, ordered from its base outward. These map
    /// to XRHandJointID.ThumbMetacarpal, ThumbProximal, ThumbDistal and ThumbTip.
    /// </summary>
    /// <remarks>
    /// The thumb has one bone fewer than the other fingers, so XR Hands has no
    /// ThumbIntermediate joint. Using a separate struct with no intermediate
    /// field means a thumb can't be passed to the four-finger curl code by mistake.
    /// </remarks>
    public struct ThumbJoints
    {
        public Vector3 metacarpal;
        public Vector3 proximal;
        public Vector3 distal;
        public Vector3 tip;

        /// <summary>
        /// False if any of the thumb's joint poses could not be read. Defaults
        /// to false, so unfilled data is treated as invalid.
        /// </summary>
        public bool isValid;
    }

    /// <summary>
    /// The minimal snapshot of one hand that the classifier needs. Open and
    /// Fist judge only the four non-thumb fingers; the thumb has its own
    /// state (see HandPoseClassifier.ClassifyThumb).
    /// </summary>
    public struct HandJointData
    {
        /// <summary>Mirrors XRHand.isTracked. Defaults to false.</summary>
        public bool isTracked;

        // Coordinate space: every joint position above and below is in tracking
        // space, i.e. local to the XR Origin's Camera Offset (the parent of the
        // tracked camera). Joint positions are never converted to world space.

        /// <summary>
        /// World rotation of the tracking space (Camera Offset), which already
        /// includes the XR Origin's rotation. The classifier applies it exactly
        /// once, to the thumb direction only. Do not also apply the XR Origin's
        /// rotation, and do not store world-space joints here.
        /// </summary>
        /// <remarks>
        /// Rotation-only conversion assumes the rig has positive, uniform scale.
        /// Nonuniform or mirrored (negative) rig scaling is not supported in this
        /// version.
        /// </remarks>
        public Quaternion trackingSpaceToWorld;

        /// <summary>
        /// False when no tracking-space rotation is available. Defaults to false,
        /// so orientation is unavailable unless the adapter supplies it.
        /// </summary>
        public bool hasTrackingSpaceToWorld;

        public ThumbJoints thumb;
        public FingerJoints index;
        public FingerJoints middle;
        public FingerJoints ring;
        public FingerJoints little;
    }
}
