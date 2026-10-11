# Barista café demo

Open **Assets/Scenes/BaristaDemo.unity** and press Play with Quest Link and
hand tracking enabled. The scene includes the tracked XR rig, both cyan hand
outlines, a stylized barista NPC, café counter, animated sign indicator above
the barista, dialogue, order summary, and up to three cups.

## Conversation

1. The barista asks: **“Welcome in! What drink can I get you?”**
2. Hold a right-hand quantity sign for 0.65 seconds: **1** = index;
   **2** = index + middle spread apart; **3** = thumb + index + middle.
   Keep the other fingers curled. The animated diagrams show each choice.
   Palm toward you is the demonstrated orientation; recognition uses the
   existing finger/thumbnail metrics and does not enforce palm orientation.
3. Nod a fist to sign **Yes**, confirming the amount. You can hold a different
   number before confirming to change it.
4. Choose **Coffee** or **Tea** with both hands visible. Coffee uses two fists,
   with the right fist above the steady left fist and grinding in a small
   horizontal circle. Tea uses a right **F hand** (thumb/index pinched together,
   other three fingers extended), dipping down and back up above a cupped
   left **C hand**. These are simple geometry checks, not template matching.
5. Sign **Please**: circle the open right palm in front of the camera. The hint
   explicitly shows the requested adaptation instead of the chest location.
6. The barista responds: **“I will get your [amount] [drink] out shortly.
   Thank you!”** The order cups appear, and the dialogue remains visible.
7. After the short response, sign **Thank You**: start the open right hand
   below the camera, then move outward. The hint says to start at the bottom
   of the camera instead of the chin. This scene requires the starting palm
   at least 5 cm below the camera.
8. **Order complete** appears. The scene stays at its end state.

## Framework and tuning

`BaristaOrderSession` owns the ordered conversation. `BaristaHandRules` calls
the existing `HandPoseClassifier` for quantities, pinch, cup and fist shapes.
`BaristaDrinkRecognizer` handles the two-hand drink motions, while
`DirectHandGestureController` supplies Yes, Please and Thank You events.
`BaristaSignHint` draws original animated hand-outline diagrams.

Quantity, drink, and motion inputs only affect their matching step. Missing
tracking cancels partial input. Each new step resets the direct gesture window,
so a previous movement cannot complete the next prompt. Confirmed order choices
survive tracking loss. The diagrams and rules are prototype demonstrations;
physical Quest tracking and sign teaching quality still need user testing.

Tune hold duration and response duration on **Barista Café Demo /
BaristaDemoController**. Tune Yes/Please/Thank You on **Hand Pose Recognition /
DirectHandGestureController**. The scene retains the saved 70/160-degree
finger curl thresholds. Drink rules currently use a 3 cm minimum grind diameter
and a 4.5 cm down-and-back-up tea dip.

In Unity Editor without an active headset, keyboard rehearsal is available:
**1 / 2 / 3** select amount, **Enter** confirms, **C / T** chooses the drink,
**P** supplies Please, **Space** supplies Thank You, and **R** restarts.
It is disabled when an XR headset is active. This lets the conversation be
checked without treating keyboard input as hand recognition.

Rebuild a separate copy with **Tools > Hand Gesture > Create Barista Demo Scene**.
The builder uses a unique scene name if one already exists and adds the scene
to Build Settings; it keeps the current startup scene. The saved demo is ready
to open directly. Its builder depends on the existing HandPoseDemo scene.

## Spirit speech bubbles

All four demo scenes also show live speech bubbles for either hand:

- Index, middle, and pinky extended; thumb and ring curled: red **GO COOGS!**.
- Index and pinky extended; thumb, middle, and ring curled: orange **HOOK 'EM HORNS!**.

Each bubble stays visible only while its exact pose is held. Releasing the pose,
losing tracking, or disabling its hand adapter clears it; repeating the pose
shows it again. Both messages appear together when different hands hold the
two poses. This does not advance or reset the barista order.

Use **Tools > Hand Gesture > Add Spirit Speech Bubbles** to wire another scene
that already has both hand adapters and an XR camera. The component copies the
right-hand display's existing finger and thumb thresholds.

## Sign references

The base handshapes and drink hints were checked against Lifeprint:
[numbers 1–10](https://www.lifeprint.com/asl101/pages-signs/n/numbers1-10.htm),
[coffee](https://www.lifeprint.com/asl101/pages-signs/c/coffee.htm), and
[tea](https://www.lifeprint.com/asl101/pages-signs/t/tea.htm).
Please and Thank You use the requested camera adaptations described above.
