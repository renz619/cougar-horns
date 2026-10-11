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

        /// <summary>The pose currently shown on the label.</summary>
        public HandPose currentPose => m_CurrentPose;

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
            if (m_HasDisplayedPose && pose == m_CurrentPose)
                return;

            m_CurrentPose = pose;
            m_Label.text = LabelFor(pose);
            m_HasDisplayedPose = true;
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
