using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Rule-based Open / Fist classifier. Pure C#: no MonoBehaviour, no scene,
    /// no XR runtime, so it runs in EditMode tests on any machine.
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

        enum FingerState
        {
            Ambiguous,
            Extended,
            Curled,
        }

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

        static FingerState JudgeFinger(in FingerJoints finger, HandPoseClassifierSettings settings)
        {
            var curl = FingerCurlDegrees(finger);

            // Comparisons with NaN are always false, so invalid fingers fall
            // through to Ambiguous.
            if (curl <= settings.extendedMaxCurlDegrees)
                return FingerState.Extended;
            if (curl >= settings.curledMinCurlDegrees)
                return FingerState.Curled;
            return FingerState.Ambiguous;
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
