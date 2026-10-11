using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Complete-sequence comparison. Reuse an instance for its bounded rolling DTW workspace; no events or hardware.</summary>
    public sealed class MotionGestureRecognizer
    {
        double[] m_PreviousCost = Array.Empty<double>(), m_CurrentCost = Array.Empty<double>();
        int[] m_PreviousLength = Array.Empty<int>(), m_CurrentLength = Array.Empty<int>();
        public int WorkspaceBytes => (m_PreviousCost.Length + m_CurrentCost.Length) * sizeof(double)
            + (m_PreviousLength.Length + m_CurrentLength.Length) * sizeof(int);

        public GestureRecognitionResult Recognize(GestureClip clip, GestureCatalogSnapshot catalog)
        {
            if (clip == null) return new GestureRecognitionResult(GestureRecognitionStatus.Unavailable, null, "No completed clip.");
            if (catalog == null) return new GestureRecognitionResult(GestureRecognitionStatus.Unavailable, clip, "No valid gesture catalog.");
            if (!TryCheckComplete(clip, out var result)) return result;
            double best = double.PositiveInfinity; GestureTemplate winner = null; int example = -1;
            bool supported = false;
            foreach (var candidate in catalog.Entries)
            {
                var definition = candidate.FrozenDefinition;
                if (!candidate.IsSupported || definition.DominantSide != clip.FrozenDefinition.DominantSide
                    || definition.MotionKind != clip.FrozenDefinition.MotionKind) continue;
                supported = true;
                if (!CheckComplete(clip, definition, out var live, out _, out _)) continue;
                for (int i = 0; i < candidate.Features.Length; i++)
                {
                    if (!TryDtwValidated(live.Samples, candidate.Features[i].Samples, definition.Settings, out var error, out _)) continue;
                    // Catalog order is ordinal and examples retain insertion order, so equal scores keep the first.
                    if (error < best) { best = error; winner = candidate; example = i; }
                }
            }
            if (winner == null) return new GestureRecognitionResult(supported ? GestureRecognitionStatus.Incomplete : GestureRecognitionStatus.NoCandidates,
                clip, supported ? "No eligible complete example comparison." : "No supported same-side motion candidates.", result.PhaseCoverage);
            double threshold = winner.FrozenDefinition.Settings.MaxError;
            bool match = best <= threshold;
            return new GestureRecognitionResult(match ? GestureRecognitionStatus.Matched : GestureRecognitionStatus.AboveThreshold,
                clip, match ? "Complete motion matched." : FormattableString.Invariant(
                    $"Best complete candidate exceeds its own threshold. Candidate={winner.GestureId}; error={best:G6}; threshold={threshold:G6}; over by={best - threshold:G6}."),
                1, winner.GestureId, best, example);
        }

        public static bool TryCheckComplete(GestureClip clip, out GestureRecognitionResult result)
        {
            if (clip == null) { result = new GestureRecognitionResult(GestureRecognitionStatus.Unavailable, null, "No clip."); return false; }
            if (clip.FrozenDefinition != null && !clip.FrozenDefinition.IsSupported)
            { result = new GestureRecognitionResult(GestureRecognitionStatus.Unsupported, clip, "This category/side/spatial evaluator is not implemented."); return false; }
            if (!clip.TryValidate(out var report))
            { result = new GestureRecognitionResult(GestureRecognitionStatus.InvalidClip, clip, report.ToString()); return false; }
            bool complete = CheckComplete(clip, clip.FrozenDefinition, out _, out var coverage, out var reason);
            result = new GestureRecognitionResult(complete ? GestureRecognitionStatus.CompleteClip : GestureRecognitionStatus.Incomplete, clip, reason, coverage);
            return complete;
        }

        static bool CheckComplete(GestureClip clip, GestureDefinition definition, out GestureFeatureSequence features, out double coverage, out string reason)
        {
            features = null; coverage = 0; reason = null;
            var settings = definition.Settings;
            if (clip.Observations.Count > settings.MaxFrames) { reason = "Candidate frame budget exceeded."; return false; }
            if (clip.DurationSeconds < settings.MinDurationSeconds || clip.DurationSeconds > settings.MaxDurationSeconds)
            { reason = "Motion duration is outside the configured complete-performance bounds."; return false; }
            for (int i = 0; i < clip.Observations.Count; i++)
            {
                double phase = clip.RelativeTime(i) / clip.DurationSeconds;
                if (!MatchesShape(clip.Observations[i].GetHand(definition.DominantSide), definition, phase >= settings.FinishPhaseStart, out reason))
                { reason = $"Clip/Frames[{i}]/Shape: " + reason; return false; }
            }
            if (!GestureFeatureExtractor.TryExtract(clip, definition, out features, out reason)) return false;
            if (features.Excursion < settings.MinExcursion || features.PathLength < settings.MinPathLength)
            { reason = "Move through the complete sign; held shapes and small jitter are insufficient."; return false; }
            return CheckPhases(features.Samples, definition, out coverage, out reason);
        }

        public static bool MatchesShape(HandFrame frame, GestureDefinition definition, bool finish, out string reason)
        {
            reason = null;
            if (definition == null || !definition.IsSupported) { reason = "Unsupported gesture gate."; return false; }
            if (!GestureFeatureExtractor.TryValidateHand(frame, definition, out reason)) return false;
            var hand = HandFrameLegacyProjection.Project(frame); var s = definition.Settings;
            float extended = finish ? s.FinishExtendedMaxCurlDegrees : s.ShapeExtendedMaxCurlDegrees;
            float curled = finish ? s.FinishCurledMinCurlDegrees : s.ShapeCurledMinCurlDegrees;
            float index = HandPoseClassifier.FingerCurlDegrees(hand.index), middle = HandPoseClassifier.FingerCurlDegrees(hand.middle),
                ring = HandPoseClassifier.FingerCurlDegrees(hand.ring), little = HandPoseClassifier.FingerCurlDegrees(hand.little);
            bool digits = definition.MotionKind == GestureMotionKind.Z
                ? index <= extended && middle >= curled && ring >= curled && little >= curled
                : little <= extended && middle >= curled && ring >= curled && index >= curled;
            if (!PoseMath.IsFinite(index) || !PoseMath.IsFinite(middle) || !PoseMath.IsFinite(ring) || !PoseMath.IsFinite(little) || !digits)
            { reason = definition.MotionKind == GestureMotionKind.Z ? "Keep the index extended and the other fingers curled." : "Keep the little finger extended and the other fingers curled."; return false; }
            float ratio = HandPoseClassifier.ThumbTipDistanceRatio(hand.thumb, hand.index);
            var policy = finish ? s.FinishThumbPolicy : s.ThumbPolicy;
            bool thumb = PoseMath.IsFinite(ratio) && (policy == GestureThumbPolicy.Curled ? ratio <= s.ThumbCurledMaxRatio : ratio < s.ThumbExtendedMinRatio);
            if (!thumb) { reason = "Thumb evidence is unavailable or outside the configured recorded variant."; return false; }
            return true;
        }

        public static bool CheckPhases(IReadOnlyList<GestureFeaturePoint> points, GestureDefinition definition, out double coverage, out string reason)
        {
            coverage = 0; reason = null;
            if (points == null || points.Count < 4) { reason = "Insufficient ordered movement."; return false; }
            var s = definition.Settings; double threshold = s.StrokeThreshold; int direction = s.SigningDirection;
            Vector3 start = points[0].Tip, end = points[points.Count - 1].Tip;
            if (definition.MotionKind == GestureMotionKind.Z)
            {
                int top = 0;
                for (int i = 1; i < points.Count && start.y - points[i].Tip.y <= s.StrokeVerticalTolerance; i++)
                {
                    // A clear reversal ends the top stroke even when the diagonal is shallow.
                    if (direction * (points[top].Tip.x - points[i].Tip.x) >= threshold) break;
                    if (direction * (points[i].Tip.x - points[top].Tip.x) > 0) top = i;
                }
                if (direction * (points[top].Tip.x - start.x) < threshold || Math.Abs(points[top].Tip.y - start.y) > s.StrokeVerticalTolerance)
                { reason = "Finish the top stroke."; return false; }
                coverage = 1.0 / 3;
                int diagonal = top;
                for (int i = top + 1; i < points.Count; i++)
                    if (direction * (points[i].Tip.x - points[diagonal].Tip.x) < 0) diagonal = i;
                if (direction * (points[top].Tip.x - points[diagonal].Tip.x) < threshold || points[top].Tip.y - points[diagonal].Tip.y < threshold)
                { reason = "Finish the downward diagonal reversal."; return false; }
                coverage = 2.0 / 3;
                if (direction * (end.x - points[diagonal].Tip.x) < threshold || Math.Abs(end.y - points[diagonal].Tip.y) > s.StrokeVerticalTolerance
                    || diagonal >= points.Count - 1)
                { reason = "Finish the bottom stroke."; return false; }
            }
            else if (definition.MotionKind == GestureMotionKind.J)
            {
                int bottom = 0;
                for (int i = 1; i < points.Count; i++) if (points[i].Tip.y < points[bottom].Tip.y) bottom = i;
                if (start.y - points[bottom].Tip.y < threshold || bottom == 0)
                { reason = "Finish the downward portion."; return false; }
                coverage = .5;
                if (bottom >= points.Count - 2 || direction * (end.x - start.x) < threshold
                    || end.y - points[bottom].Tip.y < threshold * .5)
                { reason = "Finish the curved upward hook."; return false; }
                // The descent must precede lateral hook travel; reversed hooks cannot pass on endpoint similarity.
                int descent = 0;
                while (descent < bottom && start.y - points[descent].Tip.y < threshold) descent++;
                if (Math.Abs(points[descent].Tip.x - start.x) > threshold * 2)
                { reason = "Start with the downward portion before the hook."; return false; }
            }
            else { reason = "Unsupported motion phases."; return false; }
            coverage = 1; reason = "All required motion phases are present."; return true;
        }

        /// <summary>Minimum accumulated endpoint-constrained cost / alignment length. Fixed band and run cap prevent unbounded stretching.</summary>
        public bool TryDtw(IReadOnlyList<GestureFeaturePoint> live, IReadOnlyList<GestureFeaturePoint> example, GestureMotionSettings settings,
            out double error, out int pathLength)
        {
            error = 0; pathLength = 0;
            if (live == null || example == null || settings == null || live.Count < 2 || example.Count < 2 || live.Count > 256 || example.Count > 256) return false;
            // Direct diagnostic callers get the same settings validation as catalog callers.
            var validation = new ValidationReport(); settings.Validate(validation, "DTW"); if (!validation.IsValid) return false;
            return TryDtwValidated(live, example, settings, out error, out pathLength);
        }
        bool TryDtwValidated(IReadOnlyList<GestureFeaturePoint> live, IReadOnlyList<GestureFeaturePoint> example, GestureMotionSettings settings,
            out double error, out int pathLength)
        {
            error = 0; pathLength = 0;
            int run = settings.MaxWarpRun, states = 1 + 2 * run, width = example.Count * states;
            if (m_PreviousCost.Length < width)
            { m_PreviousCost = new double[width]; m_CurrentCost = new double[width]; m_PreviousLength = new int[width]; m_CurrentLength = new int[width]; }
            for (int k = 0; k < width; k++) m_PreviousCost[k] = double.PositiveInfinity;
            for (int i = 0; i < live.Count; i++)
            {
                for (int k = 0; k < width; k++) { m_CurrentCost[k] = double.PositiveInfinity; m_CurrentLength[k] = 0; }
                for (int j = Math.Max(0, i - settings.DtwBand); j <= Math.Min(example.Count - 1, i + settings.DtwBand); j++)
                {
                    double local = LocalDistance(live[i], example[j], settings);
                    if (!PoseMath.IsFinite(local)) return false;
                    int cell = j * states;
                    if (i == 0 && j == 0) { m_CurrentCost[cell] = local; m_CurrentLength[cell] = 1; continue; }
                    if (i > 0 && j > 0)
                        for (int state = 0; state < states; state++) SetBest(cell, m_PreviousCost[(j - 1) * states + state], m_PreviousLength[(j - 1) * states + state], local);
                    if (j > 0)
                    {
                        int left = (j - 1) * states;
                        SetBest(cell + 1, m_CurrentCost[left], m_CurrentLength[left], local);
                        for (int state = run + 1; state < states; state++) SetBest(cell + 1, m_CurrentCost[left + state], m_CurrentLength[left + state], local);
                        for (int k = 2; k <= run; k++) SetBest(cell + k, m_CurrentCost[left + k - 1], m_CurrentLength[left + k - 1], local);
                    }
                    if (i > 0)
                    {
                        SetBest(cell + run + 1, m_PreviousCost[cell], m_PreviousLength[cell], local);
                        for (int state = 1; state <= run; state++) SetBest(cell + run + 1, m_PreviousCost[cell + state], m_PreviousLength[cell + state], local);
                        for (int k = 2; k <= run; k++) SetBest(cell + run + k, m_PreviousCost[cell + run + k - 1], m_PreviousLength[cell + run + k - 1], local);
                    }
                }
                var costSwap = m_PreviousCost; m_PreviousCost = m_CurrentCost; m_CurrentCost = costSwap;
                var lengthSwap = m_PreviousLength; m_PreviousLength = m_CurrentLength; m_CurrentLength = lengthSwap;
            }
            double best = double.PositiveInfinity;
            int last = (example.Count - 1) * states;
            for (int state = 0; state < states; state++)
                if (m_PreviousCost[last + state] < best) { best = m_PreviousCost[last + state]; pathLength = m_PreviousLength[last + state]; }
            if (!PoseMath.IsFinite(best) || pathLength == 0) return false;
            error = best / pathLength; return PoseMath.IsFinite(error);
        }
        void SetBest(int target, double previous, int length, double local)
        {
            double sum = previous + local;
            if (length > 0 && PoseMath.IsFinite(sum) && sum < m_CurrentCost[target])
            { m_CurrentCost[target] = sum; m_CurrentLength[target] = length + 1; }
        }
        static double LocalDistance(GestureFeaturePoint a, GestureFeaturePoint b, GestureMotionSettings s)
        {
            if (!PoseMath.IsFinite(a.Time) || !PoseMath.IsFinite(b.Time) || !PoseMath.IsFinite(a.Tip) || !PoseMath.IsFinite(b.Tip) || !PoseMath.IsFinite(a.Wrist) || !PoseMath.IsFinite(b.Wrist)
                || !PoseMath.TryNormalize(a.WristRotation, out var qa) || !PoseMath.TryNormalize(b.WristRotation, out var qb)) return double.NaN;
            // |dot| treats q and -q identically. Angular feature units are configured degrees, never Euler subtraction.
            double normA = (double)qa.x * qa.x + (double)qa.y * qa.y + (double)qa.z * qa.z + (double)qa.w * qa.w;
            double normB = (double)qb.x * qb.x + (double)qb.y * qb.y + (double)qb.z * qb.z + (double)qb.w * qb.w;
            double dot = Math.Min(1, Math.Abs((double)qa.x * qb.x + (double)qa.y * qb.y + (double)qa.z * qb.z + (double)qa.w * qb.w) / Math.Sqrt(normA * normB));
            double angle = 2 * Math.Acos(dot) * 180 / Math.PI;
            return (s.TipWeight * GestureFeatureExtractor.Distance(a.Tip, b.Tip) + s.WristWeight * GestureFeatureExtractor.Distance(a.Wrist, b.Wrist)
                + s.OrientationWeight * angle / s.OrientationScaleDegrees) / (s.TipWeight + s.WristWeight + s.OrientationWeight);
        }
    }
}
