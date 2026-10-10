using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Builds synthetic finger geometry so the classifier can be tested without
    /// a headset.
    /// </summary>
    static class FakeHandData
    {
        // Approximate adult bone lengths in meters: metacarpal, proximal,
        // intermediate, distal.
        static readonly float[] k_BoneLengths = { 0.065f, 0.040f, 0.025f, 0.020f };

        // Sideways spacing between neighboring fingers' metacarpal bases.
        const float k_FingerSpacing = 0.02f;

        /// <summary>
        /// One finger whose knuckle, middle and fingertip joints each bend by
        /// <paramref name="bendPerJointDeg"/>, so its total curl is 3x that.
        /// The finger is built pointing along +Z, bending about +X, then
        /// rotated by <paramref name="orientation"/> and moved to <paramref name="origin"/>.
        /// </summary>
        public static FingerJoints Finger(float bendPerJointDeg, Quaternion orientation, Vector3 origin)
        {
            var bend = Quaternion.AngleAxis(bendPerJointDeg, Vector3.right);
            var points = new Vector3[5];
            var direction = Vector3.forward;

            for (var i = 0; i < k_BoneLengths.Length; i++)
            {
                points[i + 1] = points[i] + direction * k_BoneLengths[i];
                direction = bend * direction;
            }

            return new FingerJoints
            {
                metacarpal = origin + orientation * points[0],
                proximal = origin + orientation * points[1],
                intermediate = origin + orientation * points[2],
                distal = origin + orientation * points[3],
                tip = origin + orientation * points[4],
                isValid = true,
            };
        }

        /// <summary>
        /// A tracked hand with a per-finger bend (index, middle, ring, little).
        /// </summary>
        public static HandJointData Hand(
            float indexBend, float middleBend, float ringBend, float littleBend,
            Quaternion orientation, Vector3 origin)
        {
            Vector3 Base(int fingerNumber) => origin + orientation * (Vector3.right * (fingerNumber * k_FingerSpacing));

            return new HandJointData
            {
                isTracked = true,
                index = Finger(indexBend, orientation, Base(0)),
                middle = Finger(middleBend, orientation, Base(1)),
                ring = Finger(ringBend, orientation, Base(2)),
                little = Finger(littleBend, orientation, Base(3)),
            };
        }

        /// <summary>A tracked hand at the origin with every finger bent equally.</summary>
        public static HandJointData Hand(float bendPerJointDeg) =>
            Hand(bendPerJointDeg, bendPerJointDeg, bendPerJointDeg, bendPerJointDeg, Quaternion.identity, Vector3.zero);
    }
}
