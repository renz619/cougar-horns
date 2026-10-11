using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Rule-based Open / Fist classifier, plus per-finger and thumb states.
    /// Pure C#: no MonoBehaviour, no scene, no XR runtime, so it runs in
    /// EditMode tests on any machine.
    /// </summary>
    /// <remarks>
    /// A static class can't be instantiated or attached to a GameObject; it is
    /// just a home for functions. Whichever component owns the settings calls
    /// Classify each frame.
    /// </remarks>
    public static class HandPoseClassifier
    {
        // Segments shorter than this (in meters) are treated as bad data.
        // Real finger bones are 2-7 cm long; coincident joints usually mean
        // the runtime gave us zeroed or placeholder positions.
        const float k_MinSegmentLength = 1e-4f;

        /// <summary>
        /// Classifies one hand from its four non-thumb fingers.
        /// </summary>
        /// <param name="hand">
        /// Joint snapshot. Passed with <c>in</c> (read-only by reference) so the
        /// struct isn't copied on every call.
        /// </param>
        /// <param name="settings">Curl thresholds.</param>
        /// <returns>
        /// Open if all four fingers are extended, Fist if all four are curled,
        /// otherwise Unknown, including for untracked, invalid or ambiguous data.
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static HandPose Classify(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!hand.isTracked)
                return HandPose.Unknown;

            // Reversed or overlapping thresholds would let one curl value count
            // as both extended and curled. Refuse to classify rather than guess.
            // Written as !(a < b) so NaN thresholds are rejected too.
            if (!(settings.extendedMaxCurlDegrees < settings.curledMinCurlDegrees))
                return HandPose.Unknown;

            var extended = 0;
            var curled = 0;
            Tally(JudgeFinger(hand.index, settings), ref extended, ref curled);
            Tally(JudgeFinger(hand.middle, settings), ref extended, ref curled);
            Tally(JudgeFinger(hand.ring, settings), ref extended, ref curled);
            Tally(JudgeFinger(hand.little, settings), ref extended, ref curled);

            if (extended == 4)
                return HandPose.Open;
            if (curled == 4)
                return HandPose.Fist;
            return HandPose.Unknown;
        }

        /// <summary>
        /// Total bend of one finger in degrees: the angle between consecutive
        /// bone segments at the knuckle (MCP), middle joint (PIP) and fingertip
        /// joint (DIP), summed. Angles between segments don't change when the
        /// whole hand moves or rotates, so this is position- and
        /// rotation-invariant.
        /// </summary>
        /// <returns>
        /// The curl in degrees, or <see cref="float.NaN"/> if the finger is
        /// marked invalid or any segment is degenerate (too short, NaN or infinite).
        /// </returns>
        public static float FingerCurlDegrees(in FingerJoints finger)
        {
            if (!finger.isValid)
                return float.NaN;

            var metacarpalBone = finger.proximal - finger.metacarpal;
            var proximalBone = finger.intermediate - finger.proximal;
            var intermediateBone = finger.distal - finger.intermediate;
            var distalBone = finger.tip - finger.distal;

            if (!IsUsableSegment(metacarpalBone) || !IsUsableSegment(proximalBone)
                || !IsUsableSegment(intermediateBone) || !IsUsableSegment(distalBone))
                return float.NaN;

            // Vector3.Angle returns the unsigned angle between two directions,
            // 0-180 degrees.
            return Vector3.Angle(metacarpalBone, proximalBone)
                + Vector3.Angle(proximalBone, intermediateBone)
                + Vector3.Angle(intermediateBone, distalBone);
        }

        /// <summary>
        /// Judges one non-thumb finger on its own, with the same thresholds and
        /// rules Classify uses. Intended for debug readouts; Classify does not
        /// call this.
        /// </summary>
        /// <returns>
        /// Extended or Curled, or Unknown if the finger is invalid, its curl
        /// falls between the thresholds, or the thresholds are reversed.
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static FingerState ClassifyFinger(in FingerJoints finger, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!(settings.extendedMaxCurlDegrees < settings.curledMinCurlDegrees))
                return FingerState.Unknown;

            return JudgeFinger(finger, settings);
        }

        /// <summary>
        /// Judges the thumb from <see cref="ThumbTipDistanceRatio"/>. Open and
        /// Fist ignore the thumb, so this never affects Classify.
        /// </summary>
        /// <returns>
        /// Extended or Curled, or Unknown if the hand isn't tracked, the thumb or
        /// index data is invalid, the ratio falls between the thresholds, or the
        /// thumb thresholds are reversed.
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static FingerState ClassifyThumb(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!hand.isTracked)
                return FingerState.Unknown;

            // Same reasoning as the finger thresholds: overlapping or NaN values
            // would make one ratio both extended and curled, so refuse to judge.
            if (!(settings.thumbCurledMaxTipDistanceRatio < settings.thumbExtendedMinTipDistanceRatio))
                return FingerState.Unknown;

            var ratio = ThumbTipDistanceRatio(hand.thumb, hand.index);

            // A NaN ratio fails both comparisons and falls through to Unknown.
            if (ratio >= settings.thumbExtendedMinTipDistanceRatio)
                return FingerState.Extended;
            if (ratio <= settings.thumbCurledMaxTipDistanceRatio)
                return FingerState.Curled;
            return FingerState.Unknown;
        }

        /// <summary>
        /// Distance from the thumb tip to the index knuckle (index proximal
        /// joint), divided by the thumb's length. A thumb sticking out reads
        /// high; a thumb tucked against or across the palm reads low.
        /// </summary>
        /// <remarks>
        /// Tucking the thumb mostly happens at its base, which bend angles at
        /// the thumb's two upper joints barely register, so a distance is used
        /// instead. Thumb length is the sum of its three bones (metacarpal,
        /// proximal, distal); there is no intermediate bone. Distances don't
        /// change when the hand moves or rotates, and dividing by thumb length
        /// removes the effect of hand size.
        /// </remarks>
        /// <returns>
        /// The ratio, or <see cref="float.NaN"/> if the thumb or index finger is
        /// marked invalid, any thumb bone is degenerate, or a position is NaN or infinite.
        /// </returns>
        public static float ThumbTipDistanceRatio(in ThumbJoints thumb, in FingerJoints index)
        {
            if (!thumb.isValid || !index.isValid)
                return float.NaN;

            var metacarpalBone = thumb.proximal - thumb.metacarpal;
            var proximalBone = thumb.distal - thumb.proximal;
            var distalBone = thumb.tip - thumb.distal;

            if (!IsUsableSegment(metacarpalBone) || !IsUsableSegment(proximalBone) || !IsUsableSegment(distalBone))
                return float.NaN;

            // The tip may legitimately touch the knuckle (distance 0), so only
            // require a finite distance here, not a minimum length.
            var tipDistance = (thumb.tip - index.proximal).magnitude;
            if (float.IsNaN(tipDistance) || float.IsInfinity(tipDistance))
                return float.NaN;

            var thumbLength = metacarpalBone.magnitude + proximalBone.magnitude + distalBone.magnitude;
            return tipDistance / thumbLength;
        }

        /// <summary>
        /// Resolves the full pose, including the v0.2a poses, from the finger
        /// states, thumb state and index-middle spread. Open and Fist come from
        /// the unchanged <see cref="Classify"/>, used as the final fallback.
        /// </summary>
        /// <remarks>
        /// Priority order, first match wins (most specific first, Open and Fist last):
        ///   1. L        thumb Extended; index Extended; middle, ring, little Curled
        ///   2. Y        thumb Extended; little Extended; index, middle, ring Curled
        ///   3. Pointing thumb NOT Extended (Curled or Unknown); index Extended;
        ///               middle, ring, little Curled
        ///   4. V        index, middle Extended and index-middle spread Apart;
        ///               ring, little Curled; thumb ignored
        ///   5. U        index, middle Extended and index-middle spread Together;
        ///               ring, little Curled; thumb ignored
        ///   6. Classify(hand, settings): Fist, Open or Unknown
        /// The current rules are mutually exclusive (L vs. Pointing differ by
        /// thumb, V vs. U by spread, and every new pose needs a Curled finger, so
        /// none overlaps Open). The order only matters once overlapping poses are
        /// added.
        /// </remarks>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static HandPose ClassifyPose(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!hand.isTracked)
                return HandPose.Unknown;

            var thumb = ClassifyThumb(hand, settings);
            var index = ClassifyFinger(hand.index, settings);
            var middle = ClassifyFinger(hand.middle, settings);
            var ring = ClassifyFinger(hand.ring, settings);
            var little = ClassifyFinger(hand.little, settings);

            const FingerState extended = FingerState.Extended;
            const FingerState curled = FingerState.Curled;

            // 1. L
            if (thumb == extended && index == extended && middle == curled && ring == curled && little == curled)
                return HandPose.L;

            // 2. Y
            if (thumb == extended && little == extended && index == curled && middle == curled && ring == curled)
                return HandPose.Y;

            // 3. Pointing
            if (thumb != extended && index == extended && middle == curled && ring == curled && little == curled)
                return HandPose.Pointing;

            // 4. V and 5. U share their finger states and differ only by spread.
            // A spread in the Unknown band matches neither and falls through.
            if (index == extended && middle == extended && ring == curled && little == curled)
            {
                var spread = ClassifySpread(hand, FingerPair.IndexMiddle, settings);
                if (spread == SpreadState.Apart)
                    return HandPose.V;
                if (spread == SpreadState.Together)
                    return HandPose.U;
            }

            // 6. Open, Fist or Unknown, exactly as before v0.2a.
            return Classify(hand, settings);
        }

        /// <summary>
        /// Judges whether an adjacent pair of extended fingers is held together
        /// or apart, from <see cref="FingerSpreadRatio"/>.
        /// </summary>
        /// <returns>
        /// Together if the ratio is at or below <c>spreadTogetherMaxRatio</c>,
        /// Apart if at or above <c>spreadApartMinRatio</c>, otherwise Unknown.
        /// Also Unknown if the ratio is NaN, either threshold is NaN or infinite,
        /// or the thresholds are equal or reversed.
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static SpreadState ClassifySpread(in HandJointData hand, FingerPair pair, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var together = settings.spreadTogetherMaxRatio;
            var apart = settings.spreadApartMinRatio;

            // !(a < b) rejects equal, reversed and NaN thresholds, but would let
            // e.g. together = -infinity through, so check finiteness explicitly.
            if (!IsFinite(together) || !IsFinite(apart) || !(together < apart))
                return SpreadState.Unknown;

            var ratio = FingerSpreadRatio(hand, pair, settings);

            // A NaN ratio fails both comparisons and falls through to Unknown.
            if (ratio <= together)
                return SpreadState.Together;
            if (ratio >= apart)
                return SpreadState.Apart;
            return SpreadState.Unknown;
        }

        /// <summary>
        /// How much farther apart a pair's fingertips are than its knuckles,
        /// measured across the palm and divided by palm width.
        /// </summary>
        /// <remarks>
        /// <c>u</c> is the knuckle line from the index proximal joint to the
        /// little proximal joint, and palm width is its length:
        /// <c>(dot(second.tip - first.tip, u) - dot(second.proximal - first.proximal, u)) / palmWidth</c>.
        ///
        /// This is extra lateral separation beyond the knuckles, not a literal
        /// "touching" test: parallel fingers read about 0 even with a gap,
        /// fingers whose tips converge read negative, and spread fingers read
        /// positive. Measuring only along the knuckle line reduces, but does
        /// not remove, the effect of one finger bending more than the other.
        /// Dot products and lengths between the hand's own joints don't change
        /// when the hand moves or rotates, and dividing by palm width removes
        /// the effect of hand size.
        /// </remarks>
        /// <returns>
        /// The ratio, or <see cref="float.NaN"/> if the hand isn't tracked,
        /// either finger of the pair isn't Extended, the index or little finger
        /// (which define the knuckle line) is invalid, palm width is degenerate,
        /// or any value is NaN or infinite.
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static float FingerSpreadRatio(in HandJointData hand, FingerPair pair, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!hand.isTracked || !TryGetPair(hand, pair, out var first, out var second))
                return float.NaN;

            // Spread is only defined for two extended fingers; never guess.
            if (ClassifyFinger(first, settings) != FingerState.Extended
                || ClassifyFinger(second, settings) != FingerState.Extended)
                return float.NaN;

            if (!hand.index.isValid || !hand.little.isValid)
                return float.NaN;

            var knuckleLine = hand.little.proximal - hand.index.proximal;
            if (!IsUsableSegment(knuckleLine))
                return float.NaN;

            var palmWidth = knuckleLine.magnitude;
            var across = knuckleLine / palmWidth;
            var tipGap = Vector3.Dot(second.tip - first.tip, across);
            var knuckleGap = Vector3.Dot(second.proximal - first.proximal, across);
            var ratio = (tipGap - knuckleGap) / palmWidth;

            return IsFinite(ratio) ? ratio : float.NaN;
        }

        /// <summary>
        /// Resolves the full pose including the v0.2b ThumbsUp, which needs the
        /// thumb's world-space direction.
        /// </summary>
        /// <remarks>
        /// Priority order, first match wins:
        ///   1. ThumbsUp  thumb Extended; index, middle, ring, little Curled;
        ///                thumb direction Up (within thumbUpMaxAngleDegrees of world +Y)
        ///   2. ClassifyPose(hand, settings), unchanged: L, Y, Pointing, V, U,
        ///      then Classify (Fist, Open or Unknown)
        /// ThumbsUp's finger states match no v0.2a rule, so without a valid
        /// upward thumb the same hand resolves to Fist. A sideways, downward,
        /// Unknown or unavailable thumb direction is never guessed into ThumbsUp.
        /// </remarks>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static HandPose ClassifyPoseWithOrientation(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!hand.isTracked)
                return HandPose.Unknown;

            // 1. ThumbsUp
            if (ClassifyThumb(hand, settings) == FingerState.Extended
                && ClassifyFinger(hand.index, settings) == FingerState.Curled
                && ClassifyFinger(hand.middle, settings) == FingerState.Curled
                && ClassifyFinger(hand.ring, settings) == FingerState.Curled
                && ClassifyFinger(hand.little, settings) == FingerState.Curled
                && ClassifyThumbDirection(hand, settings) == ThumbDirectionState.Up)
                return HandPose.ThumbsUp;

            // 2. Everything from v0.2a, exactly as before.
            return ClassifyPose(hand, settings);
        }

        /// <summary>
        /// Judges whether the thumb points up in world space, from
        /// <see cref="ThumbAngleFromWorldUpDegrees"/>.
        /// </summary>
        /// <returns>
        /// Up if the angle is at or below <c>thumbUpMaxAngleDegrees</c>
        /// (inclusive), NotUp if above it. Unknown if the angle is unavailable,
        /// or the setting is not finite or outside [0, 90).
        /// </returns>
        /// <exception cref="ArgumentNullException">settings is null.</exception>
        public static ThumbDirectionState ClassifyThumbDirection(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // Below 90 so a sideways (90) or downward thumb can never count as up.
            var maxAngle = settings.thumbUpMaxAngleDegrees;
            if (!IsFinite(maxAngle) || maxAngle < 0f || maxAngle >= 90f)
                return ThumbDirectionState.Unknown;

            var angle = ThumbAngleFromWorldUpDegrees(hand);
            if (float.IsNaN(angle))
                return ThumbDirectionState.Unknown;

            return angle <= maxAngle ? ThumbDirectionState.Up : ThumbDirectionState.NotUp;
        }

        /// <summary>
        /// Angle in degrees (0-180) between the thumb's direction and Unity
        /// world up (+Y). The direction runs from the thumb's proximal joint to
        /// its tip: the two visible thumb bones, without the metacarpal that sits
        /// inside the palm.
        /// </summary>
        /// <remarks>
        /// Joints are in tracking space (Camera Offset local). The direction is
        /// converted to world space exactly once, by
        /// <see cref="HandJointData.trackingSpaceToWorld"/>. A direction needs
        /// only rotation, so translation and positive uniform scale don't affect
        /// the result; nonuniform or mirrored rig scaling is not supported.
        /// </remarks>
        /// <returns>
        /// The angle, or <see cref="float.NaN"/> if the hand isn't tracked, the
        /// thumb is invalid, there is no tracking-space rotation, the rotation is
        /// not a finite unit quaternion, or the direction is degenerate.
        /// </returns>
        public static float ThumbAngleFromWorldUpDegrees(in HandJointData hand)
        {
            if (!hand.isTracked || !hand.thumb.isValid || !hand.hasTrackingSpaceToWorld)
                return float.NaN;

            if (!TryNormalizeRotation(hand.trackingSpaceToWorld, out var toWorld))
                return float.NaN;

            var direction = hand.thumb.tip - hand.thumb.proximal;
            if (!IsUsableSegment(direction))
                return float.NaN;

            var angle = Vector3.Angle(toWorld * direction, Vector3.up);
            return IsFinite(angle) ? angle : float.NaN;
        }

        // Accepts only finite quaternions within a small tolerance of unit
        // length. A zero (default) or clearly scaled quaternion is rejected
        // rather than silently normalized.
        static bool TryNormalizeRotation(Quaternion rotation, out Quaternion normalized)
        {
            normalized = Quaternion.identity;

            if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w))
                return false;

            var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y
                + rotation.z * rotation.z + rotation.w * rotation.w;
            if (Mathf.Abs(lengthSquared - 1f) > 1e-3f)
                return false;

            var length = Mathf.Sqrt(lengthSquared);
            normalized = new Quaternion(rotation.x / length, rotation.y / length, rotation.z / length, rotation.w / length);
            return true;
        }

        static bool TryGetPair(in HandJointData hand, FingerPair pair, out FingerJoints first, out FingerJoints second)
        {
            switch (pair)
            {
                case FingerPair.IndexMiddle:
                    first = hand.index;
                    second = hand.middle;
                    return true;
                case FingerPair.MiddleRing:
                    first = hand.middle;
                    second = hand.ring;
                    return true;
                case FingerPair.RingLittle:
                    first = hand.ring;
                    second = hand.little;
                    return true;
                default:
                    first = default;
                    second = default;
                    return false;
            }
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static FingerState JudgeFinger(in FingerJoints finger, HandPoseClassifierSettings settings)
        {
            var curl = FingerCurlDegrees(finger);

            // Comparisons with NaN are always false, so invalid fingers fall
            // through to Unknown.
            if (curl <= settings.extendedMaxCurlDegrees)
                return FingerState.Extended;
            if (curl >= settings.curledMinCurlDegrees)
                return FingerState.Curled;
            return FingerState.Unknown;
        }

        static void Tally(FingerState state, ref int extended, ref int curled)
        {
            if (state == FingerState.Extended)
                extended++;
            else if (state == FingerState.Curled)
                curled++;
        }

        static bool IsUsableSegment(Vector3 segment)
        {
            // A NaN or infinite component makes the magnitude NaN or infinite,
            // so checking the magnitude covers every component.
            var length = segment.magnitude;
            return !float.IsNaN(length) && !float.IsInfinity(length) && length >= k_MinSegmentLength;
        }
    }
}
