using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Connects the pipeline: reads the adapter's hand snapshot each frame,
    /// runs the classifier, and shows OPEN, FIST or UNKNOWN on a TextMeshPro
    /// label. The text is only rewritten when the result changes.
    /// </summary>
    /// <remarks>
    /// This component owns the classifier thresholds, so they are tuned in its
    /// Inspector. HandPoseClassifier itself stays a plain static function.
    /// </remarks>
    public class HandPoseDisplay : MonoBehaviour
    {
        [SerializeField, Tooltip("Supplies the hand joint data to classify.")]
        QuestHandAdapter m_Adapter;

        // TMP_Text is the shared base class of both TextMeshPro components
        // (3D TextMeshPro and UI TextMeshProUGUI), so either can be assigned.
        [SerializeField, Tooltip("TextMeshPro text that shows OPEN, FIST or UNKNOWN.")]
        TMP_Text m_Label;

        // Initialized here so a newly added component starts with the default
        // thresholds. Unity then saves any Inspector edits with the scene.
        [SerializeField, Tooltip("Curl thresholds used to judge each finger.")]
        HandPoseClassifierSettings m_Settings = new HandPoseClassifierSettings();

        HandPose m_CurrentPose;
        bool m_HasDisplayedPose;
        string m_CurrentHint;

        /// <summary>The pose currently shown on the label.</summary>
        public HandPose currentPose => m_CurrentPose;

        /// <summary>The tracking explanation currently shown below the pose.</summary>
        public string currentHint => m_CurrentHint;

        /// <summary>The text shown for each pose.</summary>
        public static string LabelFor(HandPose pose)
        {
            switch (pose)
            {
                case HandPose.Open:
                    return "OPEN";
                case HandPose.Fist:
                    return "FIST";
                default:
                    return "UNKNOWN";
            }
        }

        void OnEnable()
        {
            // Report setup mistakes once here instead of throwing every frame.
            if (m_Adapter == null)
                Debug.LogError($"{nameof(HandPoseDisplay)} on '{name}': no {nameof(QuestHandAdapter)} assigned. The label will show UNKNOWN.", this);

            // Force the first Refresh to write the label, whatever it says in the scene.
            m_HasDisplayedPose = false;
            Refresh();
        }

        void Update()
        {
            Refresh();
        }

        void OnDisable()
        {
            // Don't leave a stale FIST or OPEN on screen while this component is off.
            m_CurrentPose = HandPose.Unknown;
            if (m_Label != null)
                m_Label.text = LabelFor(HandPose.Unknown);
        }

        void Refresh()
        {
            if (m_Label == null)
            {
                Debug.LogError($"{nameof(HandPoseDisplay)} on '{name}': no TextMeshPro label assigned. Disabling this component.", this);
                // Disabling stops Update, so the error is logged once, not every frame.
                enabled = false;
                return;
            }

            var pose = ClassifyCurrentHand();
            var hint = TrackingHint(pose);
            if (m_HasDisplayedPose && pose == m_CurrentPose && hint == m_CurrentHint)
                return;

            m_CurrentPose = pose;
            m_CurrentHint = hint;
            m_Label.text = $"{LabelFor(pose)}\n<size=22%>{hint}</size>";
            m_HasDisplayedPose = true;
        }

        string TrackingHint(HandPose pose)
        {
            if (m_Adapter == null)
                return "Hand reader is not connected.";

            var side = m_Adapter.handedness == UnityEngine.XR.Hands.Handedness.Left ? "Left" : "Right";
            switch (m_Adapter.trackingState)
            {
                case QuestHandAdapter.TrackingState.Disabled:
                    return "Hand reader is disabled.";
                case QuestHandAdapter.TrackingState.WaitingForSubsystem:
                    return "Waiting for Quest hand tracking.\nConnect Quest Link and enable hand tracking.";
                case QuestHandAdapter.TrackingState.WaitingForUpdate:
                    return "Waiting for hand data.\nWear the headset and resume Quest Link.";
                case QuestHandAdapter.TrackingState.NotTracked:
                    return $"{side} hand is not tracked.\nEnable hand tracking, put controllers down, and show your hand.";
                case QuestHandAdapter.TrackingState.IncompleteJoints:
                    return $"{side} finger positions are incomplete.\nKeep your hand visible to the headset cameras.";
            }

            if (m_Settings == null || !(m_Settings.extendedMaxCurlDegrees < m_Settings.curledMinCurlDegrees))
                return "Recognition settings need attention.";

            var hand = m_Adapter.currentHand;
            if (float.IsNaN(HandPoseClassifier.FingerCurlDegrees(hand.index))
                || float.IsNaN(HandPoseClassifier.FingerCurlDegrees(hand.middle))
                || float.IsNaN(HandPoseClassifier.FingerCurlDegrees(hand.ring))
                || float.IsNaN(HandPoseClassifier.FingerCurlDegrees(hand.little)))
                return $"{side} finger positions are unusable.\nKeep your hand visible to the headset cameras.";

            return pose == HandPose.Unknown
                ? $"{side} hand is tracked; pose is unclear.\nOpen all four fingers or make a fist."
                : $"{side} hand is tracked.";
        }

        HandPose ClassifyCurrentHand()
        {
            // Unity overrides == null for components, so this is also true when
            // the adapter was destroyed. A disabled adapter counts as unavailable.
            if (m_Adapter == null || !m_Adapter.isActiveAndEnabled || m_Settings == null)
                return HandPose.Unknown;

            return HandPoseClassifier.Classify(m_Adapter.currentHand, m_Settings);
        }
    }
}
