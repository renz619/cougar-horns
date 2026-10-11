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
