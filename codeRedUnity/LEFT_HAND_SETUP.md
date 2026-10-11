# Left-hand open/closed display

The HandPoseDemo, HandPoseRecognitionDemo, and HandGestureMotionDemo scenes
include a separate `Left Hand State` object and `Left Hand State Label` above
the existing readouts. Reopen the saved scene if it was already open in Unity.
The left hand also uses the same cyan five-finger joint outline as the right
hand, with its own `HandJointVisualizer` reading the left adapter. Both
visualizers share the right hand's material and tracking-space reference.

- `LEFT: OPEN`: all four non-thumb fingers are extended.
- `LEFT: CLOSED`: all four non-thumb fingers are curled.
- `LEFT: UNKNOWN`: tracking is unavailable, required joints are invalid, or
  the fingers are mixed or between the thresholds.

`LeftHandStateDisplay` calls the same `HandPoseClassifier.Classify` used by the
original open/fist implementation. Thumb position is ignored. The saved demos
start with the existing tuned thresholds: extended maximum 70 degrees and
curled minimum 160 degrees. Each hand has its own adapter and label.

For another scene with an existing right-hand `HandPoseDisplay`, choose
**Tools > Hand Pose > Add Left Hand Display to Active Scene**, then save.
The command copies that display's classifier settings and tracking-space
reference, sets the new adapter's handedness to Left, and creates a dedicated
TextMeshPro label. Running it again does not duplicate the display.
It also adds or updates the left visualizer, including in scenes that already
have the left-hand display.

For manual setup, create a GameObject with `QuestHandAdapter` (Handedness =
Left, Tracking Space = XR Origin/Camera Offset) and `LeftHandStateDisplay`.
Assign that adapter to Adapter and a separate TextMeshPro text to Label.
Use the right-hand display's curl thresholds under Settings.

Headset check: open and close the left hand while changing the right hand's
pose independently. Hide the left hand and confirm UNKNOWN replaces the last
detection. Partial poses should also read UNKNOWN. Test tracking with Quest
Link and hand tracking enabled.
