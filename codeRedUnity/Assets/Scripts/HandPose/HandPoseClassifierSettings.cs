using System;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Tunable thresholds for <see cref="HandPoseClassifier"/>.
    /// </summary>
    /// <remarks>
    /// [Serializable] lets Unity save this plain C# class as part of whatever
    /// MonoBehaviour holds it in a [SerializeField] field, and draw its fields
    /// in that component's Inspector. [Range] turns a float field into a slider.
    ///
    /// "Curl" is the sum of a finger's three joint bends (knuckle + middle
    /// joint + fingertip joint), in degrees. A straight finger is near 0.
    /// The gap between the two thresholds is deliberate: a finger whose curl
    /// falls inside it is neither extended nor curled, so the hand is Unknown.
    /// </remarks>
    [Serializable]
    public class HandPoseClassifierSettings
    {
        [Tooltip("A finger whose total curl is at or below this many degrees counts as extended.")]
        [Range(0f, 360f)]
        public float extendedMaxCurlDegrees = 60f;

        [Tooltip("A finger whose total curl is at or above this many degrees counts as curled. Must be greater than the extended threshold.")]
        [Range(0f, 360f)]
        public float curledMinCurlDegrees = 160f;

        // The thumb uses a different measure: how far its tip is from the
        // index knuckle, divided by the thumb's own length. Larger means more
        // extended, the opposite direction from curl degrees. Open and Fist
        // never read these values.
        [Header("Thumb")]
        [Tooltip("The thumb counts as extended when its tip is at least this far from the index knuckle, as a fraction of thumb length.")]
        [Range(0f, 2f)]
        public float thumbExtendedMinTipDistanceRatio = 0.6f;

        [Tooltip("The thumb counts as curled when its tip is at most this far from the index knuckle, as a fraction of thumb length. Must be less than the extended threshold.")]
        [Range(0f, 2f)]
        public float thumbCurledMaxTipDistanceRatio = 0.45f;

        // Spread compares two adjacent extended fingers: how much farther apart
        // the fingertips are than the knuckles, measured across the palm and
        // divided by palm width. Parallel fingers read about 0, fingers whose
        // tips converge read negative, and spread fingers read positive. The
        // range allows negative values so Together can be tuned below 0.
        [Header("Spread")]
        [Tooltip("Two extended fingers count as together when their spread is at or below this ratio of palm width. Larger means more spread; may be negative. Must be less than the apart threshold.")]
        [Range(-1f, 2f)]
        public float spreadTogetherMaxRatio = 0.10f;

        [Tooltip("Two extended fingers count as apart when their spread is at or above this ratio of palm width. Larger means more spread.")]
        [Range(-1f, 2f)]
        public float spreadApartMinRatio = 0.25f;
    }
}
