using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>A minimal left-hand readout using the same four-finger rules as the right hand.</summary>
    public sealed class LeftHandStateDisplay : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Adapter;
        [SerializeField] TMP_Text m_Label;
        [SerializeField] HandPoseClassifierSettings m_Settings = new HandPoseClassifierSettings();

        public HandPose currentPose { get; private set; }

        // Keep the decision in the existing pure classifier. Thumb position and
        // orientation do not change whether the four fingers are open or closed.
        public static HandPose Classify(in HandJointData hand, HandPoseClassifierSettings settings)
            => settings == null ? HandPose.Unknown : HandPoseClassifier.Classify(hand, settings);

        public static string LabelFor(HandPose pose)
            => pose == HandPose.Open ? "LEFT: OPEN"
                : pose == HandPose.Fist ? "LEFT: CLOSED" : "LEFT: UNKNOWN";

        void OnEnable() => Refresh();
        void Update() => Refresh();
        void OnDisable() => Show(HandPose.Unknown);

        void Refresh()
        {
            bool available = m_Adapter != null && m_Adapter.isActiveAndEnabled
                && m_Adapter.HandSide == HandSide.Left;
            Show(available ? Classify(m_Adapter.currentHand, m_Settings) : HandPose.Unknown);
        }

        void Show(HandPose pose)
        {
            currentPose = pose;
            if (m_Label != null && m_Label.text != LabelFor(pose))
                m_Label.text = LabelFor(pose);
        }
    }
}
