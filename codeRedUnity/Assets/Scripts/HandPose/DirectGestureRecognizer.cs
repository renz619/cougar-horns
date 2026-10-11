using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public enum DirectGesture { None, ThankYou, Please, Yes }

    [Serializable]
    public sealed class DirectGestureSettings
    {
        [Min(.1f)] public float windowSeconds = 3f;
        [Min(.05f)] public float minimumDuration = .25f;
        [Min(.05f)] public float maximumSampleGap = .25f;
        [Min(0)] public float cooldownSeconds = 1f;
        [Header("Thank You: open hand near camera, then outward")]
        [Min(.01f)] public float thankYouStartDistance = .5f;
        [Tooltip("Maximum starting palm height relative to the camera. Negative values require a start below the camera.")]
        public float thankYouStartMaximumHeight = .5f;
        [Min(.01f)] public float thankYouOutwardDistance = .22f;
        [Min(.01f)] public float thankYouLateralTolerance = .18f;
        [Header("Please: open palm circle in the camera's XY plane")]
        [Min(.01f)] public float circleMinimumRadius = .06f;
        [Min(.01f)] public float circleMaximumRadius = .3f;
        [Range(270, 360)] public float circleMinimumDegrees = 300f;
        [Min(.01f)] public float circleDepthTolerance = .15f;
        [Header("Yes: fist tilt down/up or up/down")]
        [Range(5, 90)] public float nodMinimumDegrees = 25f;
        [Range(1, 30)] public float nodReturnTolerance = 12f;
        [Min(.01f)] public float nodMaximumTravel = .2f;

        internal bool IsValid()
        {
            return Positive(windowSeconds) && Positive(minimumDuration) && minimumDuration < windowSeconds
                && Positive(maximumSampleGap) && PoseMath.IsFinite(cooldownSeconds) && cooldownSeconds >= 0
                && Positive(thankYouStartDistance) && PoseMath.IsFinite(thankYouStartMaximumHeight)
                && Positive(thankYouOutwardDistance) && Positive(thankYouLateralTolerance)
                && Positive(circleMinimumRadius) && Positive(circleMaximumRadius) && circleMinimumRadius < circleMaximumRadius
                && PoseMath.IsFinite(circleMinimumDegrees) && circleMinimumDegrees >= 270 && circleMinimumDegrees <= 360
                && Positive(circleDepthTolerance) && Positive(nodMinimumDegrees) && nodMinimumDegrees <= 90
                && Positive(nodReturnTolerance) && nodReturnTolerance < nodMinimumDegrees && Positive(nodMaximumTravel);
        }
        static bool Positive(float value) => PoseMath.IsFinite(value) && value > 0;
    }

    // Position and palm pitch use a camera reference frozen at the start of
    // each attempt. Moving the headset alone cannot draw a gesture.
    public readonly struct DirectGestureSample
    {
        public readonly double Time;
        public readonly Vector3 Palm;
        public readonly float PitchDegrees;
        public readonly HandPose Shape;
        public DirectGestureSample(double time, Vector3 palm, float pitchDegrees, HandPose shape)
        { Time = time; Palm = palm; PitchDegrees = pitchDegrees; Shape = shape; }
    }

    /// <summary>Bounded trajectory checks; no recordings, catalog, or template matching.</summary>
    public sealed class DirectGestureRecognizer
    {
        readonly List<DirectGestureSample> m_Samples = new List<DirectGestureSample>(180);
        double m_LastTime = -1;
        double m_ReadyAt;
        public int SampleCount => m_Samples.Count;
        public string Status { get; private set; } = "Show an open hand or fist.";

        public void Reset(string reason = "Tracking unavailable.")
        { m_Samples.Clear(); m_LastTime = -1; m_ReadyAt = 0; Status = reason; }

        public DirectGesture Update(DirectGestureSample sample, DirectGestureSettings settings)
        {
            if (settings == null || !settings.IsValid() || !PoseMath.IsFinite(sample.Time) || sample.Time < 0
                || !PoseMath.IsFinite(sample.Palm) || !PoseMath.IsFinite(sample.PitchDegrees)
                || (sample.Shape != HandPose.Open && sample.Shape != HandPose.Fist))
            { Reset("Need valid tracking and an open hand or fist."); return DirectGesture.None; }
            if (sample.Time == m_LastTime) return DirectGesture.None; // Never count a frame twice.
            if (sample.Time < m_LastTime) { Reset("Sample clock changed; start again."); return DirectGesture.None; }
            if (m_LastTime >= 0 && sample.Time - m_LastTime > settings.maximumSampleGap)
                m_Samples.Clear();
            m_LastTime = sample.Time;
            if (sample.Time < m_ReadyAt) { m_Samples.Clear(); Status = "Ready for the next gesture shortly."; return DirectGesture.None; }
            if (m_Samples.Count > 0 && (sample.Shape != m_Samples[0].Shape
                || sample.Time - m_Samples[0].Time > settings.windowSeconds || m_Samples.Count >= 180))
            { m_Samples.Clear(); Status = "Start a new gesture."; return DirectGesture.None; }
            m_Samples.Add(sample);
            Status = sample.Shape == HandPose.Fist ? "Fist: nod down and back up." : "Open hand: move outward or draw a circle.";
            if (m_Samples.Count < 6) return DirectGesture.None;

            // Check every possible start in the bounded window. This tolerates
            // positioning the hand before beginning the deliberate gesture.
            for (int start = 0; start <= m_Samples.Count - 6; start++)
            {
                if (sample.Time - m_Samples[start].Time < settings.minimumDuration) break;
                var result = sample.Shape == HandPose.Fist
                    ? (IsNod(start, settings) ? DirectGesture.Yes : DirectGesture.None)
                    : IsCircle(start, settings) ? DirectGesture.Please
                    : IsOutward(start, settings) ? DirectGesture.ThankYou : DirectGesture.None;
                if (result == DirectGesture.None) continue;
                m_Samples.Clear(); m_ReadyAt = sample.Time + settings.cooldownSeconds;
                Status = LabelFor(result) + " detected.";
                return result;
            }
            return DirectGesture.None;
        }

        bool IsOutward(int start, DirectGestureSettings settings)
        {
            var first = m_Samples[start].Palm;
            var last = m_Samples[m_Samples.Count - 1].Palm;
            if (first.z < 0 || first.y > settings.thankYouStartMaximumHeight || first.magnitude > settings.thankYouStartDistance
                || last.z - first.z < settings.thankYouOutwardDistance) return false;
            float previousZ = first.z;
            for (int i = start + 1; i < m_Samples.Count; i++)
            {
                var p = m_Samples[i].Palm;
                if (new Vector2(p.x - first.x, p.y - first.y).magnitude > settings.thankYouLateralTolerance
                    || p.z < previousZ - .025f || p.z < first.z - .025f) return false;
                previousZ = p.z;
            }
            return true;
        }

        bool IsCircle(int start, DirectGestureSettings settings)
        {
            var minimum = m_Samples[start].Palm;
            var maximum = minimum;
            for (int i = start; i < m_Samples.Count; i++)
            {
                var p = m_Samples[i].Palm;
                if (p.z < .05f || p.z > .85f || new Vector2(p.x, p.y).magnitude > .65f) return false;
                minimum = Vector3.Min(minimum, p); maximum = Vector3.Max(maximum, p);
            }
            var extent = (maximum - minimum) * .5f;
            if (extent.x < settings.circleMinimumRadius || extent.y < settings.circleMinimumRadius
                || extent.x > settings.circleMaximumRadius || extent.y > settings.circleMaximumRadius
                || Mathf.Min(extent.x, extent.y) / Mathf.Max(extent.x, extent.y) < .5f
                || maximum.z - minimum.z > settings.circleDepthTolerance) return false;
            var center = (minimum + maximum) * .5f;
            float signed = 0, absolute = 0;
            Vector2 previous = default;
            for (int i = start; i < m_Samples.Count; i++)
            {
                var p = m_Samples[i].Palm - center;
                var radial = new Vector2(p.x / extent.x, p.y / extent.y);
                if (radial.magnitude < .55f || radial.magnitude > 1.5f) return false;
                if (i > start)
                {
                    float angle = Vector2.SignedAngle(previous, radial);
                    if (Mathf.Abs(angle) > 70f) return false;
                    signed += angle; absolute += Mathf.Abs(angle);
                }
                previous = radial;
            }
            return Mathf.Abs(signed) >= settings.circleMinimumDegrees && Mathf.Abs(signed) >= absolute * .85f
                && Vector3.Distance(m_Samples[start].Palm, m_Samples[m_Samples.Count - 1].Palm)
                    <= Mathf.Max(.04f, Mathf.Min(extent.x, extent.y) * .75f);
        }

        bool IsNod(int start, DirectGestureSettings settings)
        {
            var first = m_Samples[start];
            var last = m_Samples[m_Samples.Count - 1];
            float minimum = first.PitchDegrees, maximum = minimum;
            for (int i = start; i < m_Samples.Count; i++)
            {
                var sample = m_Samples[i];
                if (Vector3.Distance(first.Palm, sample.Palm) > settings.nodMaximumTravel) return false;
                minimum = Mathf.Min(minimum, sample.PitchDegrees); maximum = Mathf.Max(maximum, sample.PitchDegrees);
            }
            return maximum - minimum >= settings.nodMinimumDegrees
                && Mathf.Abs(last.PitchDegrees - first.PitchDegrees) <= settings.nodReturnTolerance;
        }

        public static string LabelFor(DirectGesture gesture)
            => gesture == DirectGesture.ThankYou ? "THANK YOU" : gesture == DirectGesture.Please ? "PLEASE"
                : gesture == DirectGesture.Yes ? "YES" : "UNKNOWN";
    }
}
