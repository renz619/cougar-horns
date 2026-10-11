using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace HandPoseRecognition
{
    public sealed class BaristaDemoController : MonoBehaviour
    {
        [SerializeField] QuestHandAdapter m_Right;
        [SerializeField] QuestHandAdapter m_Left;
        [SerializeField] DirectHandGestureController m_Gestures;
        [SerializeField] Transform m_TrackingSpace;
        [SerializeField] Transform m_Head;
        [SerializeField] TMP_Text m_Dialogue;
        [SerializeField] TMP_Text m_Hint;
        [SerializeField] TMP_Text m_Progress;
        [SerializeField] TMP_Text m_Step;
        [SerializeField] GameObject[] m_HintGroups;
        [SerializeField] GameObject[] m_Cups;
        [SerializeField] Transform m_BaristaHead;
        [SerializeField] HandPoseClassifierSettings m_Settings = new HandPoseClassifierSettings();
        [SerializeField, Min(.1f)] float m_AmountHoldSeconds = .65f;
        [SerializeField, Min(1)] float m_ResponseSeconds = 3.5f;
        [SerializeField] bool m_EditorKeyboardRehearsal = true;
        readonly BaristaOrderSession m_Order = new BaristaOrderSession();
        readonly BaristaDrinkRecognizer m_Drinks = new BaristaDrinkRecognizer();
        int m_HeldAmount;
        double m_HoldStarted, m_ResponseStarted;
        double m_LastAmountTime = -1;
        ulong m_AmountSequence, m_RightSequence, m_LeftSequence;
        Quaternion m_DrinkReference;
        Vector3 m_SupportOrigin;
        Matrix4x4 m_LastRig;
        bool m_HasRig;
        string m_InputHint = "Show your right hand to the headset cameras.";
        public BaristaOrderSession Order => m_Order;

        void OnEnable()
        {
            m_Order.Restart();
            if (m_Gestures != null) m_Gestures.GestureDetected += OnGesture;
            Changed();
        }
        void OnDisable() { if (m_Gestures != null) m_Gestures.GestureDetected -= OnGesture; }
        void OnGesture(DirectGesture gesture) { if (m_Order.Gesture(gesture)) Changed(); }

        void Changed()
        {
            m_HeldAmount = 0; m_LastAmountTime = -1; m_AmountSequence = m_RightSequence = m_LeftSequence = 0;
            m_Drinks.Reset(); m_Gestures?.ResetRecognition();
            m_ResponseStarted = Time.unscaledTimeAsDouble;
            m_InputHint = "";
            if (m_HintGroups != null)
            {
                int group = m_Order.Step == BaristaOrderStep.Amount ? 0 : m_Order.Step == BaristaOrderStep.ConfirmAmount ? 1
                    : m_Order.Step == BaristaOrderStep.Drink ? 2 : m_Order.Step == BaristaOrderStep.Please ? 3
                    : m_Order.Step == BaristaOrderStep.ThankYou ? 4 : -1;
                for (int i = 0; i < m_HintGroups.Length; i++) if (m_HintGroups[i] != null) m_HintGroups[i].SetActive(i == group);
            }
            if (m_Cups != null)
                for (int i = 0; i < m_Cups.Length; i++) if (m_Cups[i] != null)
                    m_Cups[i].SetActive(i < m_Order.Amount && m_Order.Step >= BaristaOrderStep.BaristaResponse);
            RefreshText();
        }

        void Update()
        {
            if (m_TrackingSpace != null)
            {
                var rig = m_TrackingSpace.localToWorldMatrix;
                bool changed = false;
                if (m_HasRig) for (int i = 0; i < 16; i++) changed |= Mathf.Abs(rig[i] - m_LastRig[i]) > 1e-5f;
                if (changed) { m_HeldAmount = 0; m_Drinks.Reset(); }
                m_LastRig = rig; m_HasRig = true;
            }
            if (m_Order.Step == BaristaOrderStep.Amount || m_Order.Step == BaristaOrderStep.ConfirmAmount) ReadAmount();
            else if (m_Order.Step == BaristaOrderStep.Drink) ReadDrink();
            else if (m_Order.Step == BaristaOrderStep.BaristaResponse && Time.unscaledTimeAsDouble - m_ResponseStarted >= m_ResponseSeconds)
            { if (m_Order.FinishResponse()) Changed(); }
            RehearseKeyboard();
            if (m_BaristaHead != null)
                m_BaristaHead.localRotation = Quaternion.Euler(Mathf.Sin(Time.unscaledTime * 1.4f) * 2, Mathf.Sin(Time.unscaledTime * .6f) * 3, 0);
            RefreshText();
        }

        bool Read(QuestHandAdapter adapter, HandSide side, out HandFrame frame)
        {
            frame = null;
            return adapter != null && adapter.isActiveAndEnabled && adapter.HandSide == side
                && adapter.TryGetLatestFrame(out frame) && frame != null && frame.IsValid && frame.HandSide == side
                && frame.Space == HandFrameSpace.TrackingSpace && Time.realtimeSinceStartupAsDouble - frame.TimestampSeconds <= .25;
        }

        void ReadAmount()
        {
            if (m_Settings == null || !PoseMath.IsFinite(m_AmountHoldSeconds) || m_AmountHoldSeconds <= 0
                || !Read(m_Right, HandSide.Right, out var frame))
            { m_HeldAmount = 0; m_InputHint = "Right hand not tracked. Keep your hand visible."; return; }
            if (frame.SequenceId == m_AmountSequence) return;
            m_AmountSequence = frame.SequenceId;
            if (m_LastAmountTime >= 0 && (frame.TimestampSeconds <= m_LastAmountTime || frame.TimestampSeconds - m_LastAmountTime > .25)) m_HeldAmount = 0;
            m_LastAmountTime = frame.TimestampSeconds;
            var hand = HandFrameLegacyProjection.Project(frame);
            int amount = BaristaHandRules.Amount(hand, m_Settings);
            if (amount == 0) { m_HeldAmount = 0; m_InputHint = m_Order.Step == BaristaOrderStep.ConfirmAmount
                ? "Make a fist and nod it to confirm, or hold a different number to change it." : "Hold 1, 2, or 3 with your right hand."; return; }
            if (amount != m_HeldAmount) { m_HeldAmount = amount; m_HoldStarted = frame.TimestampSeconds; }
            float fraction = Mathf.Clamp01((float)((frame.TimestampSeconds - m_HoldStarted) / m_AmountHoldSeconds));
            m_InputHint = "Reading " + amount + "  |  " + Mathf.RoundToInt(fraction * 100) + "%";
            if (fraction >= 1 && (m_Order.Step == BaristaOrderStep.Amount || m_Order.Amount != amount))
            { if (m_Order.ChooseAmount(amount)) Changed(); }
        }

        void ReadDrink()
        {
            if (m_TrackingSpace == null || m_Head == null || m_Settings == null
                || !Read(m_Right, HandSide.Right, out var right) || !Read(m_Left, HandSide.Left, out var left)
                || System.Math.Abs(right.TimestampSeconds - left.TimestampSeconds) > .08
                || !right.TryGetJoint(HandJointId.Palm, out var rp) || !left.TryGetJoint(HandJointId.Palm, out var lp))
            { m_Drinks.Reset(); m_InputHint = "Show BOTH hands: right above left."; return; }
            if (right.SequenceId == m_RightSequence || left.SequenceId == m_LeftSequence) return;
            m_RightSequence = right.SequenceId; m_LeftSequence = left.SequenceId;
            var rh = HandFrameLegacyProjection.Project(right); var lh = HandFrameLegacyProjection.Project(left);
            bool coffee = HandPoseClassifier.Classify(rh, m_Settings) == HandPose.Fist && HandPoseClassifier.Classify(lh, m_Settings) == HandPose.Fist;
            bool tea = BaristaHandRules.TeaPinch(rh, m_Settings) && BaristaHandRules.Cup(lh);
            var support = m_TrackingSpace.TransformPoint(lp.Position);
            if (m_Drinks.SampleCount == 0)
            { m_DrinkReference = Quaternion.Euler(0, m_Head.eulerAngles.y, 0); m_SupportOrigin = support; }
            if (Vector3.Distance(support, m_SupportOrigin) > .1f)
            { m_Drinks.Reset(); m_InputHint = "Keep your left supporting hand steady."; return; }
            var relative = Quaternion.Inverse(m_DrinkReference) * (m_TrackingSpace.TransformPoint(rp.Position) - support);
            var drink = m_Drinks.Update(System.Math.Max(right.TimestampSeconds, left.TimestampSeconds), relative, coffee, tea);
            m_InputHint = m_Drinks.Status;
            if (drink != BaristaDrink.None && m_Order.ChooseDrink(drink)) Changed();
        }

        void RehearseKeyboard()
        {
            if (!Application.isEditor || !m_EditorKeyboardRehearsal || XRSettings.isDeviceActive || Keyboard.current == null) return;
            var keys = Keyboard.current;
            if (keys.rKey.wasPressedThisFrame) { m_Order.Restart(); Changed(); }
            int amount = keys.digit1Key.wasPressedThisFrame ? 1 : keys.digit2Key.wasPressedThisFrame ? 2 : keys.digit3Key.wasPressedThisFrame ? 3 : 0;
            if (amount != 0 && m_Order.ChooseAmount(amount)) Changed();
            if (keys.enterKey.wasPressedThisFrame) OnGesture(DirectGesture.Yes);
            var drink = keys.cKey.wasPressedThisFrame ? BaristaDrink.Coffee : keys.tKey.wasPressedThisFrame ? BaristaDrink.Tea : BaristaDrink.None;
            if (drink != BaristaDrink.None && m_Order.ChooseDrink(drink)) Changed();
            if (keys.pKey.wasPressedThisFrame) OnGesture(DirectGesture.Please);
            if (keys.spaceKey.wasPressedThisFrame) OnGesture(DirectGesture.ThankYou);
        }

        void RefreshText()
        {
            var step = m_Order.Step;
            string title = step == BaristaOrderStep.Amount ? "01 / HOW MANY?" : step == BaristaOrderStep.ConfirmAmount ? "CONFIRM " + m_Order.Amount
                : step == BaristaOrderStep.Drink ? "02 / COFFEE OR TEA?" : step == BaristaOrderStep.Please ? "03 / SAY PLEASE"
                : step == BaristaOrderStep.BaristaResponse ? "ORDER RECEIVED" : step == BaristaOrderStep.ThankYou ? "04 / SAY THANK YOU" : "ORDER COMPLETE";
            string hint = step == BaristaOrderStep.Amount ? "Choose 1, 2, or 3. Palm toward you; hold the sign steady."
                : step == BaristaOrderStep.ConfirmAmount ? "You chose " + m_Order.Amount + ". Sign YES: nod your fist to confirm."
                : step == BaristaOrderStep.Drink ? "COFFEE: grind two fists.  TEA: dip an F-hand into a cupped hand."
                : step == BaristaOrderStep.Please ? "Circle your open palm IN FRONT OF THE CAMERA, instead of on your chest."
                : step == BaristaOrderStep.BaristaResponse ? "Your order is on its way..."
                : step == BaristaOrderStep.ThankYou ? "Open hand from the BOTTOM OF THE CAMERA, then outward. Start here instead of your chin."
                : "Thank you received. End of scene.";
            Set(m_Step, title); Set(m_Dialogue, m_Order.Dialogue); Set(m_Hint, hint);
            Set(m_Progress, (m_Order.Amount == 0 ? "YOUR ORDER" : m_Order.Order.ToUpperInvariant())
                + (string.IsNullOrEmpty(m_InputHint) || (step != BaristaOrderStep.Amount && step != BaristaOrderStep.ConfirmAmount && step != BaristaOrderStep.Drink) ? "" : "\n" + m_InputHint));
        }
        static void Set(TMP_Text label, string text) { if (label != null && label.text != text) label.text = text; }
    }
}
