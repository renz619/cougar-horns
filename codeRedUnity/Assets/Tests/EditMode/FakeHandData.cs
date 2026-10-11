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

        // Approximate thumb bone lengths in meters: metacarpal, proximal, distal.
        // Total 0.102 m.
        static readonly float[] k_ThumbBoneLengths = { 0.045f, 0.032f, 0.025f };

        // Fingers are spaced along +X starting with the index at the origin, so
        // the thumb base sits just to the -X side of the index metacarpal.
        static readonly Vector3 k_ThumbBase = new Vector3(-0.02f, 0f, 0f);

        /// <summary>
        /// A thumb in the same local space as <c>Hand(...)</c> built at the origin
        /// with no rotation. The first bone points <paramref name="headingDeg"/>
        /// away from the index (from +Z toward -X); each of the two thumb joints
        /// then bends <paramref name="bendPerJointDeg"/> back toward the index.
        /// </summary>
        public static ThumbJoints Thumb(float headingDeg, float bendPerJointDeg)
        {
            var points = new Vector3[4];
            points[0] = k_ThumbBase;
            var heading = headingDeg;

            for (var i = 0; i < k_ThumbBoneLengths.Length; i++)
            {
                // A negative angle about +Y turns +Z toward -X.
                var direction = Quaternion.AngleAxis(-heading, Vector3.up) * Vector3.forward;
                points[i + 1] = points[i] + direction * k_ThumbBoneLengths[i];
                heading -= bendPerJointDeg;
            }

            return new ThumbJoints
            {
                metacarpal = points[0],
                proximal = points[1],
                distal = points[2],
                tip = points[3],
                isValid = true,
            };
        }

        // Proximal joint to tip: proximal + intermediate + distal bones.
        const float k_ReachFromKnuckle = 0.040f + 0.025f + 0.020f;

        // Index proximal to little proximal: three finger spacings.
        const float k_PalmWidth = 3f * k_FingerSpacing;

        /// <summary>
        /// A tracked hand at the origin, like <c>Hand(...)</c>, where each finger
        /// can also be splayed sideways. Splay turns the finger about the palm
        /// normal (+Y) <b>at its proximal joint</b>, so the knuckles and palm width
        /// stay fixed. Positive splay turns toward the little finger (+X).
        /// The thumb is left unset (invalid).
        /// </summary>
        public static HandJointData SplayedHand(
            float indexBend, float middleBend, float ringBend, float littleBend,
            float indexSplayDeg, float middleSplayDeg, float ringSplayDeg, float littleSplayDeg)
        {
            FingerJoints SplayedFinger(int fingerNumber, float bend, float splayDeg)
            {
                var finger = Finger(bend, Quaternion.identity, Vector3.right * (fingerNumber * k_FingerSpacing));
                var splay = Quaternion.AngleAxis(splayDeg, Vector3.up);
                var knuckle = finger.proximal;
                finger.intermediate = knuckle + splay * (finger.intermediate - knuckle);
                finger.distal = knuckle + splay * (finger.distal - knuckle);
                finger.tip = knuckle + splay * (finger.tip - knuckle);
                return finger;
            }

            return new HandJointData
            {
                isTracked = true,
                index = SplayedFinger(0, indexBend, indexSplayDeg),
                middle = SplayedFinger(1, middleBend, middleSplayDeg),
                ring = SplayedFinger(2, ringBend, ringSplayDeg),
                little = SplayedFinger(3, littleBend, littleSplayDeg),
            };
        }

        /// <summary>
        /// The splay angle s that gives index-middle spread <paramref name="ratio"/>
        /// when both fingers are straight and splayed -s (index) and +s (middle):
        /// spread = 2 * reach * sin(s) / palmWidth.
        /// </summary>
        public static float SplayForRatio(float ratio) =>
            Mathf.Asin(ratio * k_PalmWidth / (2f * k_ReachFromKnuckle)) * Mathf.Rad2Deg;

        /// <summary>
        /// A copy of <paramref name="hand"/> with an available tracking-space
        /// to world rotation, as the adapter would supply from Camera Offset.
        /// </summary>
        public static HandJointData WithTrackingSpace(HandJointData hand, Quaternion trackingSpaceToWorld)
        {
            hand.trackingSpaceToWorld = trackingSpaceToWorld;
            hand.hasTrackingSpaceToWorld = true;
            return hand;
        }

        /// <summary>
        /// A tracking-space rotation that turns <paramref name="hand"/>'s thumb
        /// direction (proximal to tip) to world up, then tilts it
        /// <paramref name="angleFromUpDeg"/> away from up about a horizontal axis
        /// chosen by <paramref name="azimuthDeg"/>. The resulting angle from world
        /// up equals <paramref name="angleFromUpDeg"/> (0 = up, 90 = sideways,
        /// 180 = down).
        /// </summary>
        public static Quaternion ThumbUpRotation(HandJointData hand, float angleFromUpDeg, float azimuthDeg = 0f)
        {
            var direction = hand.thumb.tip - hand.thumb.proximal;
            var toUp = Quaternion.FromToRotation(direction, Vector3.up);
            var tiltAxis = Quaternion.AngleAxis(azimuthDeg, Vector3.up) * Vector3.right;
            return Quaternion.AngleAxis(angleFromUpDeg, tiltAxis) * toUp;
        }

        /// <summary>A copy of <paramref name="hand"/> with the given thumb.</summary>
        public static HandJointData WithThumb(HandJointData hand, ThumbJoints thumb)
        {
            hand.thumb = thumb;
            return hand;
        }

        /// <summary>
        /// A copy of <paramref name="hand"/> with every joint scaled about the
        /// origin, then rotated, then moved. Simulates a bigger or smaller hand
        /// somewhere else in the room.
        /// </summary>
        public static HandJointData Transformed(HandJointData hand, Quaternion rotation, Vector3 translation, float scale)
        {
            Vector3 Map(Vector3 point) => translation + rotation * (point * scale);

            FingerJoints MapFinger(FingerJoints finger)
            {
                finger.metacarpal = Map(finger.metacarpal);
                finger.proximal = Map(finger.proximal);
                finger.intermediate = Map(finger.intermediate);
                finger.distal = Map(finger.distal);
                finger.tip = Map(finger.tip);
                return finger;
            }

            hand.thumb.metacarpal = Map(hand.thumb.metacarpal);
            hand.thumb.proximal = Map(hand.thumb.proximal);
            hand.thumb.distal = Map(hand.thumb.distal);
            hand.thumb.tip = Map(hand.thumb.tip);
            hand.index = MapFinger(hand.index);
            hand.middle = MapFinger(hand.middle);
            hand.ring = MapFinger(hand.ring);
            hand.little = MapFinger(hand.little);
            return hand;
        }
    }
}
