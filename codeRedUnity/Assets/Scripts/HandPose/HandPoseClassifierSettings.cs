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
    }
}
