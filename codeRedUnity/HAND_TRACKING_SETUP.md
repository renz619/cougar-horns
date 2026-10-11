# Hand Pose Demo

Open `Assets/Scenes/HandPoseDemo.unity` and use Unity Play mode with Quest Link.
The default reader tracks your **right hand**. The demo shows a pose label and
five cyan outlines (including the thumb) when real joint positions are available.
The thumb outline connects its four joints using the same renderer and material
as the other fingers. The thumb's state appears in the debug text, but it does
not affect the current OPEN/FIST classifier. The scene does not display
a camera video feed.

## Headset check

1. Connect Quest Link and enter its headset view.
2. Enable hand tracking on the Quest 2, put the controllers down, and wear the
   headset. Keep your bare right hand visible to the headset cameras.
   For development over Link, also check the PC Link application's Developer
   Runtime Features setting. Meta's current instructions place it under
   Settings > Developer (older versions may use a different section):
   https://developers.meta.com/vr/documentation/unity/unity-link/
   Restart Unity after enabling Developer Runtime Features so the XR session
   initializes with the updated Link configuration.
3. Start Unity Play mode. Open all four fingers, then make a fist.
4. The label should change to `OPEN` or `FIST`. Moving your hand out of tracking
   should show `UNKNOWN` and hide the finger outlines.

`UNKNOWN` now includes an explanation:

- **Waiting for Quest hand tracking:** there is no running XR hand subsystem.
  Check Quest Link, headset hand tracking, and the Standalone OpenXR Hand
  Tracking Subsystem feature. An idle XR session will not supply hand data.
- **Waiting for hand data:** the subsystem started but no Dynamic hand update
  has reached the reader yet. Wear the headset and resume the Link session.
- **Right hand is not tracked:** updates are arriving, but the runtime reports
  the right hand as untracked. Put controllers down and show your bare hand.
- **Finger positions are incomplete/unusable:** the runtime is not supplying
  usable positions for all required finger joints.
- **Hand is tracked; pose is unclear:** joint data is available, but the fingers
  do not all satisfy either the open or fist thresholds. Only in this case is
  threshold tuning a possible next step.

## Scene references

Select **Hand Pose Recognition** in the Hierarchy:

- `QuestHandAdapter`: Handedness = Right (change to Left to test the left hand).
- `HandPoseDisplay`: Adapter = the same object's QuestHandAdapter; Label =
  `XR Origin / Camera Offset / Main Camera / Pose Label Canvas / Pose Label`.
- `HandJointVisualizer`: Adapter = the same object's QuestHandAdapter;
  Tracking Space = `XR Origin / Camera Offset`; Material =
  `Assets/Materials/HandPoseJoints.mat`.

The outline uses positions from the adapter itself, so visible moving fingers
confirm that the same data source used by recognition is producing joint data.
It uses a URP Unlit material rather than the sample's incompatible legacy hand
material. The scene builder also assigns these references for newly generated
demos; rebuilding the scene is not needed after this fix.

## Verification

Use **Window > General > Test Runner > EditMode > Run All**. Tests cover the
classifier, status changes while the pose remains UNKNOWN, measured finger
positions in tracking space (including the thumb), invalid fingers, and hiding
outlines on tracking loss. Synthetic tests do not replace a real headset test.
