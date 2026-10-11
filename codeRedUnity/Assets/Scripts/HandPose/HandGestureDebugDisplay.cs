using System.Globalization;
using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Separate developer label/trail; never writes HandPoseDisplay's label or maps gesture IDs to the legacy enum.</summary>
    public sealed class HandGestureDebugDisplay : MonoBehaviour
    {
        [SerializeField] HandGestureController m_Controller;
        [SerializeField] TMP_Text m_Label;
        [SerializeField] LineRenderer m_Trail;
        public HandGestureController Controller { get => m_Controller; set => m_Controller = value; }
        public TMP_Text Label { get => m_Label; set => m_Label = value; }
        public LineRenderer Trail { get => m_Trail; set => m_Trail = value; }
        string m_LastText;
        public static string Describe(HandGestureController controller)
        {
            if (controller == null || !controller.isActiveAndEnabled) return "MOTION UNAVAILABLE\nEnable and assign the gesture controller.";
            if (controller.Mode != GestureLessonMode.JZOnly) return "MOTION DISABLED\nSelect JZOnly deliberately after recording complete examples.";
            var session = controller.Session; var result = session.LatestObservation;
            string score = result.HasScore ? result.Error.ToString("0.0000", CultureInfo.InvariantCulture) : "n/a";
            return (session.ConfirmedGestureId == null ? "UNKNOWN" : controller.ConfirmedDisplayName + " (" + session.ConfirmedGestureId + ")")
                + $"\n{session.State}: {session.Reason}\nCandidate {session.PendingGestureId ?? "none"}; frames {session.FrameCount}; {session.DurationSeconds:F2}s"
                + $"\nPhases {session.PhaseCoverage:0.00}; error {score}; example {result.ExampleIndex}; best {result.BestCandidateId ?? "none"}";
        }
        void Update()
        {
            string text = Describe(m_Controller);
            if (m_Label != null && text != m_LastText) { m_Label.text = text; m_LastText = text; }
            if (m_Trail == null) return;
            if (m_Controller == null || !m_Controller.isActiveAndEnabled || m_Controller.Mode != GestureLessonMode.JZOnly
                || m_Controller.ReferenceProvider == null || m_Controller.ReferenceProvider.TrackingSpace == null)
            { m_Trail.positionCount = 0; return; }
            var frames = m_Controller.Session.Frames;
            if (frames.Count == 0) { m_Trail.positionCount = 0; return; }
            var joint = m_Controller.Session.TrajectoryJoint;
            var space = m_Controller.ReferenceProvider.TrackingSpace;
            m_Trail.useWorldSpace = true; m_Trail.positionCount = frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                var raw = frames[i].Right;
                if (raw != null && raw.TryGetJoint(joint, out var tip)) m_Trail.SetPosition(i, space.TransformPoint(tip.Position));
            }
        }
        void OnDisable() { if (m_Trail != null) m_Trail.positionCount = 0; if (m_Label != null) m_Label.text = "MOTION UNAVAILABLE"; m_LastText = null; }
    }
}
