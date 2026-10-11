using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    public static class BaristaHandRules
    {
        public static int Amount(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (!hand.isTracked || settings == null) return 0;
            var index = HandPoseClassifier.ClassifyFinger(hand.index, settings);
            var middle = HandPoseClassifier.ClassifyFinger(hand.middle, settings);
            if (index != FingerState.Extended || HandPoseClassifier.ClassifyFinger(hand.ring, settings) != FingerState.Curled
                || HandPoseClassifier.ClassifyFinger(hand.little, settings) != FingerState.Curled) return 0;
            var thumb = HandPoseClassifier.ClassifyThumb(hand, settings);
            if (middle == FingerState.Curled && thumb == FingerState.Curled) return 1;
            if (middle != FingerState.Extended || HandPoseClassifier.ClassifySpread(hand, FingerPair.IndexMiddle, settings) != SpreadState.Apart) return 0;
            if (thumb == FingerState.Curled) return 2;
            return thumb == FingerState.Extended ? 3 : 0;
        }

        public static bool TeaPinch(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (!hand.isTracked || settings == null || !hand.thumb.isValid || !hand.index.isValid) return false;
            float width = Vector3.Distance(hand.index.proximal, hand.little.proximal);
            float pinch = Vector3.Distance(hand.thumb.tip, hand.index.tip);
            return PoseMath.IsFinite(width) && width > .01f && PoseMath.IsFinite(pinch) && pinch <= width * .45f
                && PoseMath.IsFinite(HandPoseClassifier.FingerCurlDegrees(hand.index))
                && HandPoseClassifier.ClassifyFinger(hand.middle, settings) == FingerState.Extended
                && HandPoseClassifier.ClassifyFinger(hand.ring, settings) == FingerState.Extended
                && HandPoseClassifier.ClassifyFinger(hand.little, settings) == FingerState.Extended;
        }

        public static bool Cup(in HandJointData hand)
        {
            if (!hand.isTracked || !hand.thumb.isValid || !hand.index.isValid) return false;
            float gap = Vector3.Distance(hand.thumb.tip, hand.index.tip);
            return PoseMath.IsFinite(gap) && gap >= .015f && gap <= .15f
                && Bent(hand.index) && Bent(hand.middle) && Bent(hand.ring) && Bent(hand.little);
        }
        static bool Bent(FingerJoints finger)
        {
            float curl = HandPoseClassifier.FingerCurlDegrees(finger);
            return PoseMath.IsFinite(curl) && curl >= 40f;
        }
    }

    /// <summary>Simple two-hand drink motions measured relative to the supporting hand.</summary>
    public sealed class BaristaDrinkRecognizer
    {
        readonly List<Vector3> m_Path = new List<Vector3>(240);
        BaristaDrink m_Candidate;
        double m_Start, m_Last = -1;
        public int SampleCount => m_Path.Count;
        public string Status { get; private set; } = "Show both hands.";
        public void Reset() { m_Path.Clear(); m_Candidate = BaristaDrink.None; m_Last = -1; Status = "Show both hands."; }

        public BaristaDrink Update(double time, Vector3 relative, bool coffeeShape, bool teaShape)
        {
            var candidate = coffeeShape ? BaristaDrink.Coffee : teaShape ? BaristaDrink.Tea : BaristaDrink.None;
            if (!PoseMath.IsFinite(time) || !PoseMath.IsFinite(relative) || candidate == BaristaDrink.None
                || relative.y < -.025f || relative.y > .25f || new Vector2(relative.x, relative.z).magnitude > .2f)
            { Reset(); return BaristaDrink.None; }
            if (time == m_Last) return BaristaDrink.None;
            if (time < m_Last || (m_Last >= 0 && time - m_Last > .25) || candidate != m_Candidate
                || m_Path.Count >= 240 || (m_Path.Count > 0 && time - m_Start > 3)) Reset();
            if (m_Path.Count == 0) { m_Start = time; m_Candidate = candidate; }
            m_Last = time; m_Path.Add(relative);
            Status = candidate == BaristaDrink.Coffee ? "Two fists: grind the upper fist in a small circle." : "Pinch thumb + index; dip into your cupped left hand and back up.";
            if (m_Path.Count < 8 || time - m_Start < .35) return BaristaDrink.None;
            for (int start = 0; start <= m_Path.Count - 8; start++)
                if (candidate == BaristaDrink.Coffee ? Circle(start) : Dip(start))
                { Reset(); return candidate; }
            return BaristaDrink.None;
        }

        bool Circle(int start)
        {
            Vector3 min = m_Path[start], max = min;
            for (int i = start; i < m_Path.Count; i++) { min = Vector3.Min(min, m_Path[i]); max = Vector3.Max(max, m_Path[i]); }
            Vector3 size = max - min, center = (max + min) * .5f;
            if (size.x < .03f || size.z < .03f || size.x > .24f || size.z > .24f || size.y > .12f
                || Mathf.Min(size.x, size.z) / Mathf.Max(size.x, size.z) < .45f) return false;
            float angle = 0, total = 0;
            Vector2 previous = default;
            for (int i = start; i < m_Path.Count; i++)
            {
                Vector3 p = m_Path[i] - center;
                var radial = new Vector2(p.x / size.x, p.z / size.z);
                if (radial.magnitude < .2f) return false;
                if (i > start)
                {
                    float delta = Vector2.SignedAngle(previous, radial);
                    if (Mathf.Abs(delta) > 70) return false;
                    angle += delta; total += Mathf.Abs(delta);
                }
                previous = radial;
            }
            return Mathf.Abs(angle) >= 280 && Mathf.Abs(angle) >= total * .8f
                && Vector3.Distance(m_Path[start], m_Path[m_Path.Count - 1]) < Mathf.Min(size.x, size.z) * .5f;
        }

        bool Dip(int start)
        {
            var first = m_Path[start]; var last = m_Path[m_Path.Count - 1];
            float lowest = first.y;
            for (int i = start; i < m_Path.Count; i++)
            {
                if (new Vector2(m_Path[i].x - first.x, m_Path[i].z - first.z).magnitude > .08f) return false;
                lowest = Mathf.Min(lowest, m_Path[i].y);
            }
            return first.y - lowest >= .045f && last.y - lowest >= .045f && Mathf.Abs(last.y - first.y) <= .04f;
        }
    }
}
