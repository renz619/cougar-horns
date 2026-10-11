using System.Globalization;
using TMPro;
using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>
    /// Connects the pipeline: reads the adapter's hand snapshot each frame,
    /// runs the classifier, and shows the pose (OPEN, FIST, POINTING, V, U, Y,
    /// L or UNKNOWN) on a TextMeshPro label, with a tracking hint and small
    /// debug lines for the five finger states and the finger-pair spreads.
    /// The text is only rewritten when what it shows changes.
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
        string m_CurrentFingerStates;
        string m_CurrentSpreads;

        /// <summary>The pose currently shown on the label.</summary>
        public HandPose currentPose => m_CurrentPose;

        /// <summary>The tracking explanation currently shown below the pose.</summary>
        public string currentHint => m_CurrentHint;

        /// <summary>
        /// The debug line of all five finger states currently shown, or an
        /// empty string when there is no usable adapter.
        /// </summary>
        public string currentFingerStates => m_CurrentFingerStates;

        /// <summary>
        /// The debug line of finger-pair spreads currently shown, or an empty
        /// string when there is no usable adapter.
        /// </summary>
        public string currentSpreads => m_CurrentSpreads;

        /// <summary>
        /// Debug text listing all five finger states, with the thumb's raw
        /// tip-distance ratio and each other finger's total curl in degrees,
        /// so thresholds can be tuned on the headset, e.g.
        /// "Thumb Extended (0.72) | Index Curled (240.00°) | ...".
        /// Measurements read "n/a" when tracking or the required joint data is invalid.
        /// </summary>
        public static string DescribeFingerStates(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            var ratio = hand.isTracked ? HandPoseClassifier.ThumbTipDistanceRatio(hand.thumb, hand.index) : float.NaN;

            // InvariantCulture always prints a decimal point; some Windows locales
            // would otherwise print 0,72.
            var ratioText = float.IsNaN(ratio) ? "n/a" : ratio.ToString("0.00", CultureInfo.InvariantCulture);

            return $"Thumb {HandPoseClassifier.ClassifyThumb(hand, settings)} ({ratioText})"
                + $" | Index {DescribeFinger(hand, hand.index, settings)}"
                + $" | Middle {DescribeFinger(hand, hand.middle, settings)}"
                + $" | Ring {DescribeFinger(hand, hand.ring, settings)}"
                + $" | Little {DescribeFinger(hand, hand.little, settings)}";
        }

        static string DescribeFinger(in HandJointData hand, in FingerJoints finger, HandPoseClassifierSettings settings)
        {
            var state = hand.isTracked ? HandPoseClassifier.ClassifyFinger(finger, settings) : FingerState.Unknown;
            var curl = hand.isTracked ? HandPoseClassifier.FingerCurlDegrees(finger) : float.NaN;
            var curlText = float.IsNaN(curl) ? "n/a" : curl.ToString("0.00", CultureInfo.InvariantCulture) + "°";
            return $"{state} ({curlText})";
        }

        /// <summary>
        /// Debug text listing the spread of each adjacent finger pair with its
        /// raw ratio, so the spread thresholds can be tuned on the headset, e.g.
        /// "Idx-Mid Apart (0.42) | Mid-Rng Unknown (n/a) | Rng-Lit Unknown (n/a)".
        /// The ratio reads "n/a" when the pair isn't evaluated: the hand isn't
        /// tracked, either finger isn't Extended, or the data is invalid.
        /// </summary>
        public static string DescribeSpreads(in HandJointData hand, HandPoseClassifierSettings settings)
        {
            return $"Idx-Mid {DescribeSpread(hand, FingerPair.IndexMiddle, settings)}"
                + $" | Mid-Rng {DescribeSpread(hand, FingerPair.MiddleRing, settings)}"
                + $" | Rng-Lit {DescribeSpread(hand, FingerPair.RingLittle, settings)}";
        }

        static string DescribeSpread(in HandJointData hand, FingerPair pair, HandPoseClassifierSettings settings)
        {
            var ratio = HandPoseClassifier.FingerSpreadRatio(hand, pair, settings);
            return $"{HandPoseClassifier.ClassifySpread(hand, pair, settings)} ({FormatRatio(ratio)})";
        }

        static string FormatRatio(float ratio)
        {
            if (float.IsNaN(ratio))
                return "n/a";

            // Spread can be negative. Round first so a tiny negative value such
            // as -0.001 shows as "0.00" rather than "-0.00". -0f == 0f is true,
            // so the assignment replaces negative zero with positive zero.
            var rounded = Mathf.Round(ratio * 100f) / 100f;
            if (rounded == 0f)
                rounded = 0f;
            return rounded.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>The text shown for each pose.</summary>
        public static string LabelFor(HandPose pose)
        {
            switch (pose)
            {
                case HandPose.Open:
                    return "OPEN";
                case HandPose.Fist:
                    return "FIST";
                case HandPose.Pointing:
                    return "POINTING";
                case HandPose.V:
                    return "V";
                case HandPose.U:
                    return "U";
                case HandPose.Y:
                    return "Y";
                case HandPose.L:
                    return "L";
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
            var fingerStates = CurrentFingerStates();
            var spreads = CurrentSpreads();

            // Comparing the formatted strings means the label is only rebuilt
            // when something visible changes, e.g. the ratio's second decimal.
            if (m_HasDisplayedPose && pose == m_CurrentPose && hint == m_CurrentHint
                && fingerStates == m_CurrentFingerStates && spreads == m_CurrentSpreads)
                return;

            m_CurrentPose = pose;
            m_CurrentHint = hint;
            m_CurrentFingerStates = fingerStates;
            m_CurrentSpreads = spreads;
            m_Label.text = fingerStates.Length == 0
                ? $"{LabelFor(pose)}\n<size=22%>{hint}</size>"
                : $"{LabelFor(pose)}\n<size=22%>{hint}</size>\n<size=16%>{fingerStates}</size>\n<size=16%>{spreads}</size>";
            m_HasDisplayedPose = true;
        }

        string CurrentSpreads()
        {
            // Shown under the same conditions as the finger-state line.
            if (m_Adapter == null || !m_Adapter.isActiveAndEnabled || m_Settings == null)
                return string.Empty;

            return DescribeSpreads(m_Adapter.currentHand, m_Settings);
        }

        string CurrentFingerStates()
        {
            // Only shown with an enabled adapter; the hint already explains the
            // missing or disabled cases.
            if (m_Adapter == null || !m_Adapter.isActiveAndEnabled || m_Settings == null)
                return string.Empty;

            return DescribeFingerStates(m_Adapter.currentHand, m_Settings);
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

            // ClassifyPose checks the v0.2a poses first, then falls back to the
            // unchanged Classify for Open, Fist or Unknown.
            return HandPoseClassifier.ClassifyPose(m_Adapter.currentHand, m_Settings);
        }
    }
}
