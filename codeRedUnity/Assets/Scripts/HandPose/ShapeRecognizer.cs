using System;

namespace HandPoseRecognition
{
    /// <summary>Stateless O(samples * 26) position matcher. Caller normalizes once and builds the catalog separately.</summary>
    public static class ShapeRecognizer
    {
        public static RecognitionResult Recognize(HandFrame canonical, PoseDatabaseSnapshot catalog,
            double nowSeconds, double maxFrameAgeSeconds)
        {
            var admission = FrameAdmission.Check(canonical, nowSeconds, maxFrameAgeSeconds, true);
            if (admission != RecognitionStatus.Matched) return new RecognitionResult(admission, canonical, admission.ToString());
            if (catalog == null) return new RecognitionResult(RecognitionStatus.CatalogUnavailable, canonical, "No validated snapshot.");
            var candidates = catalog.GetCandidates(canonical.HandSide);
            if (candidates.Count == 0) return new RecognitionResult(RecognitionStatus.NoCandidates, canonical, "No same-side templates.");
            PoseTemplate winner = null;
            double best = double.PositiveInfinity;
            int bestSample = -1;
            for (int p = 0; p < candidates.Count; p++)
            {
                var pose = candidates[p];
                if (pose.HandSide != canonical.HandSide || pose.JointLayoutVersion != canonical.JointLayoutVersion
                    || pose.NormalizationVersion != canonical.NormalizationVersion) continue;
                for (int s = 0; s < pose.Samples.Count; s++)
                {
                    if (!TryError(canonical, pose.Samples[s], pose, out double error)) continue;
                    int order = winner == null ? -1 : StringComparer.Ordinal.Compare(pose.PoseId, winner.PoseId);
                    if (error < best || (error == best && (order < 0 || (order == 0 && s < bestSample))))
                    { winner = pose; best = error; bestSample = s; }
                }
            }
            if (winner == null) return new RecognitionResult(RecognitionStatus.NoEligibleSamples, canonical, "Required positive-weight positions unavailable.");
            var status = best <= winner.MaxError ? RecognitionStatus.Matched : RecognitionStatus.AboveThreshold;
            return new RecognitionResult(status, canonical, status == RecognitionStatus.Matched ? "Matched" : "Above threshold", winner.PoseId, best, bestSample);
        }

        static bool TryError(HandFrame live, PoseSample sample, PoseTemplate pose, out double error)
        {
            double sum = 0, denominator = 0;
            error = 0;
            if (sample.HandSide != live.HandSide || sample.Space != live.Space
                || sample.JointLayoutVersion != live.JointLayoutVersion || sample.NormalizationVersion != live.NormalizationVersion) return false;
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                double weight = pose.JointWeights[i];
                if (weight == 0) continue;
                if (!PoseMath.IsFinite(weight) || weight < 0
                    || !sample.TryGetJoint((HandJointId)i, out var p) || !live.TryGetJoint((HandJointId)i, out var q)
                    || !PoseMath.IsFinite(p.Position) || !PoseMath.IsFinite(q.Position)) return false;
                double x = (double)p.Position.x - q.Position.x;
                double y = (double)p.Position.y - q.Position.y;
                double z = (double)p.Position.z - q.Position.z;
                sum += weight * Math.Sqrt(x * x + y * y + z * z);
                denominator += weight;
                if (!PoseMath.IsFinite(sum) || !PoseMath.IsFinite(denominator)) return false;
            }
            if (denominator <= 0) return false;
            error = sum / denominator;
            return PoseMath.IsFinite(error);
        }
    }
}
