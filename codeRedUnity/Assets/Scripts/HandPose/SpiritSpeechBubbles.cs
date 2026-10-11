using UnityEngine;

namespace HandPoseRecognition
{
    public enum SpiritPose { None, GoCoogs, HookEmHorns }

    /// <summary>Exact five-finger poses; each bubble follows either hand without latching.</summary>
    public sealed class SpiritSpeechBubbles : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Left;
        [SerializeField] QuestHandAdapter m_Right;
        [SerializeField] GameObject m_CoogsBubble;
        [SerializeField] GameObject m_HornsBubble;
        [SerializeField] HandPoseClassifierSettings m_Settings = new HandPoseClassifierSettings();

        public static SpiritPose Classify(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            if (!hand.isTracked || settings == null
                || HandPoseClassifier.ClassifyThumb(hand, settings) != FingerState.Curled
                || HandPoseClassifier.ClassifyFinger(hand.index, settings) != FingerState.Extended
                || HandPoseClassifier.ClassifyFinger(hand.ring, settings) != FingerState.Curled
                || HandPoseClassifier.ClassifyFinger(hand.little, settings) != FingerState.Extended)
                return SpiritPose.None;
            var middle = HandPoseClassifier.ClassifyFinger(hand.middle, settings);
            return middle == FingerState.Extended ? SpiritPose.GoCoogs
                : middle == FingerState.Curled ? SpiritPose.HookEmHorns : SpiritPose.None;
        }

        void OnEnable() => Refresh();
        void Update() => Refresh();
        void OnDisable() => Show(SpiritPose.None, SpiritPose.None);

        SpiritPose Read(QuestHandAdapter adapter, HandSide side)
            => adapter != null && adapter.isActiveAndEnabled && adapter.HandSide == side
                ? Classify(adapter.currentHand, m_Settings) : SpiritPose.None;

        void Refresh() => Show(Read(m_Left, HandSide.Left), Read(m_Right, HandSide.Right));

        void Show(SpiritPose left, SpiritPose right)
        {
            SetVisible(m_CoogsBubble, left == SpiritPose.GoCoogs || right == SpiritPose.GoCoogs);
            SetVisible(m_HornsBubble, left == SpiritPose.HookEmHorns || right == SpiritPose.HookEmHorns);
        }

        static void SetVisible(GameObject bubble, bool visible)
        {
            if (bubble != null && bubble.activeSelf != visible) bubble.SetActive(visible);
        }
    }
}
