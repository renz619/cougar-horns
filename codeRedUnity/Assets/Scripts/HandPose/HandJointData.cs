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
    /// The minimal snapshot of one hand that the classifier needs. The thumb is
    /// intentionally omitted: v0.1 judges only the four non-thumb fingers.
    /// </summary>
    public struct HandJointData
    {
        /// <summary>Mirrors XRHand.isTracked. Defaults to false.</summary>
        public bool isTracked;

        public FingerJoints index;
        public FingerJoints middle;
        public FingerJoints ring;
        public FingerJoints little;
    }
}
