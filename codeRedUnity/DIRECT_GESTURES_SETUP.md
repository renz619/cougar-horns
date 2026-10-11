# Direct Thank You, Please, and Yes gestures

All three demo scenes now use `DirectHandGestureController` on the right hand.
It calls plain C# rules in `DirectGestureRecognizer`, with no gesture recording
or catalog needed. The previous J/Z controller and its labels/trail are disabled
in the motion demo. Existing recorded assets are retained.

Reopen the saved scene in Unity and enter Play with Quest Link hand tracking.
The cyan `Direct Gesture Label` below the static pose display shows a confirmed
gesture for two seconds, plus the current tracking/action hint. Both hands keep
their cyan finger outlines and the left-hand open/closed label.

- **THANK YOU:** start with an open hand near the camera (palm within 0.5 m,
  in front of it), then move at least 0.22 m outward along the camera's forward
  direction. Keep lateral movement within 0.18 m. Moving inward does not qualify.
- **PLEASE:** keep an open hand in front of the camera and draw an approximately
  circular path in the camera's horizontal/vertical plane. Either direction
  works. Default radius is 0.06–0.30 m, with at least 300 degrees of consistent
  turning and a return close to the starting point. Forward/backward depth
  variation must stay within 0.15 m. This follows the requested camera-circle
  interaction for this prototype.
- **YES:** keep a fist and tilt it down then back up, or up then back down.
  The palm's wrist-to-middle-knuckle direction must change by at least 25 degrees
  and return within 12 degrees of its starting pitch. Keep palm travel within
  0.20 m. Holding a fist still or translating it without tilting does not qualify.

An attempt lasts at most three seconds, takes at least 0.25 seconds and six
distinct samples, and uses a camera reference frozen at the start. Headset
movement alone does not create hand motion. Tracking loss, gaps over 0.25 s,
invalid joints, hand-shape changes, or XR rig changes discard an unfinished
attempt. A one-second cooldown follows detection.

Tune **Motion Settings** on `DirectHandGestureController` in the Inspector.
The hand-shape classifier uses the existing saved 70/160-degree curl thresholds
and ignores thumb position. The rules measure real joint geometry, so these
initial movement thresholds still need testing with the physical headset.

For another scene, use **Tools > Hand Gesture > Use Direct Thank You Please Yes
Rules**, then save. The scene must have a right-hand `HandPoseDisplay`, a tracked
Main Camera, and an assigned tracking space. The command creates one dedicated
gesture label, copies the hand-shape settings, and disables the old J/Z motion
controller and feedback. Repeating it does not create duplicates.

For manual wiring, add `DirectHandGestureController` to the right-hand recognition
object. Set Adapter to its right-hand `QuestHandAdapter`, Tracking Space to XR
Origin/Camera Offset, Head to Main Camera, and Label to a dedicated TMP text.
The `GestureDetected` C# event delivers `ThankYou`, `Please`, or `Yes` for other
game logic, independently of the two-second display.
