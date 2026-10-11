# Project: Unity Quest 2 Hand Pose Prototype

## Working rules
- Verify any XR Hands API usage against the installed package source in
  Library/PackageCache before using it. Never invent methods.
- Only implement what the current version asks for. Do not build ahead.
- Work one script at a time and explain where each one fits.
- Keep core logic in plain C# functions so it can be tested without a headset.
- Unity version: 6000.0.34f1. Mac is for code and tests; hand tracking is
  tested by a teammate on Windows with Quest Link.
  Known limits for ASL handshapes: poses that differ mainly in orientation
(H/U, K/P, G/Q) or thumb placement (A, E, M, N, S, T) are not reliably
separable from static joint geometry on Quest 2 and are deferred.


## Current version: v0.2b (Thumbs-up orientation)
Status: specification ready for planning; implementation is not yet approved.
v0.2a Phase 2 is complete per the developer's report. The developer reported
126 passing Unity EditMode tests and no Console errors after v0.2a Phase 1.
Spec precedence and current tuning
- This section defines the active scope. Earlier version sections are
  historical; their orientation exclusions do not prohibit v0.2b work.
  Their compatibility constraints still apply unless explicitly superseded here.
- During v0.2a headset tuning, extendedMaxCurlDegrees was raised to tolerate
  minor finger curl, especially the pinky. Its new numeric value was not
  supplied with this spec. Read the current project scene and Inspector
  settings; preserve those values. Do not restore the historical 60-degree
  default. Record actual tuned curl, thumb, and spread values here when verified.
- Scene-serialized settings override script defaults. Preserve both existing
  scene values and existing defaults during orientation implementation.
- B and 5 remain deferred. Pointing, V, U, Y, and L retain their v0.2a rules.
Goal and recognition rule
Recognize a static ThumbsUp pose using existing finger states plus the thumb's
world-space direction. For this version, "up" means Unity world +Y,
independent of headset tilt or the hand's local axes.
ThumbsUp requires ALL of the following:
- The hand is tracked and the required joint data is valid.
- Thumb state is Extended under the existing thumb classifier.
- Index, middle, ring, and little finger states are all Curled.
- A valid thumb direction points within the configured angle of world up.
ThumbsUp takes precedence over the Fist fallback. If the complete rule does
not match, retain v0.2a pose resolution. In particular, four valid Curled
non-thumb fingers can still produce Fist when the thumb is sideways,
downward, Unknown, or its orientation is unavailable. Untracked hands remain
Unknown. Unknown data must never be guessed into a ThumbsUp match.
In scope
- Propose and justify a thumb direction derived from existing thumb joints.
  Choose its endpoints during planning after inspecting actual joint data.
- Inspect and document the coordinate space used by QuestHandAdapter.
  Explain the exact conversion to world space through the existing XR rig,
  including applicable origin/parent transforms. Transform exactly once.
- Verify every XR Hands API against the installed Library/PackageCache.
  Inspect the relevant installed XR origin implementation when needed.
- Keep orientation math and pose decisions in pure C#, with no MonoBehaviour
  or scene/Transform lookup in the classifier. Isolate scene/transform access
  in the adapter or another minimal integration component.
- Expose an orientation setting in HandPoseClassifierSettings with a clear
  header and tooltip. Use a maximum angle from world up, in degrees; a larger
  angle is more permissive. Propose a provisional default during planning.
  Define the boundary as inclusive and validate a finite angle from 0 up to,
  but not including, 90 degrees so sideways/downward directions cannot qualify.
- Reject missing joints, NaN/infinite coordinates, zero-length or otherwise
  degenerate direction vectors, and invalid orientation settings for ThumbsUp.
  Expose invalid orientation as Unknown/unavailable; preserve fallback rules.
- Append ThumbsUp to HandPose without renumbering existing enum values.
- Add a THUMBS UP display label and a separate compact orientation debug
  readout showing the angle from world up and validity; use n/a when unavailable.
- Provide exact Unity Editor setup instructions for any new references.
Protected behavior
- Do not change the bodies of Classify, Tally, FingerCurlDegrees,
  ClassifyThumb, or ThumbTipDistanceRatio.
- Preserve existing public call behavior used by old tests. Add new orientation
  resolution code; do not require scene access for existing classifier calls.
- Preserve the thumb metric, all finger/spread rules, and tuned thresholds.
- The legacy Classify result for Fist/Open stays unchanged. The new orientation
  resolver may return ThumbsUp ahead of Fist only when the full new rule matches.
- Preserve existing finger-state text, raw thumb ratio, spread debug output,
  tracking behavior, and the five-finger visualization.
- All existing EditMode test methods must remain unedited and pass. New tests
  and additive fake-data helpers are allowed. Inspect the FULL git diff,
  including additions, to verify protected bodies and old tests are untouched.
Out of scope
- Other orientation-dependent signs, including H/U, K/P, and G/Q distinctions.
- Thumb-placement distinctions A/E/M/N/S/T; B/5; thumbs-down recognition.
- Dynamic signs, motion, smoothing, temporal recognition, two-hand signs.
- Recording, templates, databases, lessons, or a general recognition framework.
- Android setup or unrelated XR/sample visualization changes.
Workflow and phases
Read this file and applicable AGENTS.md first. Start in Plan mode: list exact
files, direction endpoints, coordinate conversion, settings, tests, and Editor
setup. Do not write implementation code until the developer approves the plan.
Work one script at a time, explain its role, and stop after each phase.
1. Pure orientation logic, settings, pose resolution, and new EditMode tests.
   Verify on Mac, report actual Unity test results separately from compilation
   checks, inspect the full diff, then commit after the tests pass. Stop.
2. Adapter/world-space conversion and display integration, with new integration
   tests where appropriate. Verify in Unity, then on Quest Link with the teammate.
   Tune the new orientation threshold, save it in the scene, and record its
   actual value here or deliberately synchronize its script default. Commit
   after verification and stop. Do not proceed into v0.3.
Required tests and headset verification
- Clean ThumbsUp; sideways/downward directions must not produce ThumbsUp.
- Correct direction with thumb Curled/Unknown or any non-thumb finger
  Extended/Unknown must not produce ThumbsUp.
- Angles just inside, exactly at, and just outside the allowed boundary.
- Invalid angular settings, missing tracking/joints, NaN/infinite coordinates,
  and zero-length direction. Verify fallback behavior as well as rejection.
- Translation and positive uniform-scale invariance for valid geometry.
- Rotation toward/away from world up changes orientation recognition correctly;
  rotation about world up preserves the angle. Cover non-identity XR origin
  rotation in integration tests to detect missing or double transformation.
- ThumbsUp wins over Fist when both shape rules qualify; otherwise Fist and
  Open remain available, and Pointing/V/U/Y/L retain their prior results.
- All old and new Unity EditMode tests pass with no red Console errors.
- On Quest Link, repeat upward, sideways, and downward thumb poses; verify
  headset tilt does not redefine up. Confirm the existing poses still work
  with the saved curl/thumb/spread tuning (thumb ratio remains unchanged).
- Record the tested orientation angle range and final saved threshold.


## v0.2a spec (Finger spread + non-orientation poses)
Goal: the classifier measures spread between adjacent extended fingers and
uses it, together with the per-finger states (Extended, Curled, Unknown),
to recognize new static poses. This fixes the known v0.1 limitation:
index and middle fingers touching are currently indistinguishable from
index and middle fingers apart (ASL U vs. V).

In scope:
- Spread measure for adjacent finger pairs (index-middle, middle-ring,
  ring-little). Defined only when BOTH fingers in the pair are Extended;
  otherwise it returns Unknown/NaN. Must be rotation-, translation- and
  hand-size-invariant (propose a normalization, e.g. divide by palm width
  measured index proximal to little proximal, and justify the choice
  against alternatives such as the angle between finger direction vectors).
- Spread state per pair: Together, Apart, or Unknown. An Unknown band sits
  between the two thresholds, like the thumb's. Invalid joints, reversed
  thresholds, or NaN thresholds return Unknown.
- New poses, built from finger states plus spread:
  - Pointing (ASL 1): index Extended; middle, ring, little Curled; thumb
    must NOT be Extended (Curled or Unknown allowed). This separates it
    from L.
  - V: index and middle Extended and spread Apart; ring and little Curled.
    Thumb ignored.
  - U: index and middle Extended and spread Together; ring and little
    Curled. Thumb ignored.
  - Y: thumb and little Extended; index, middle, ring Curled.
  - L: thumb and index Extended; middle, ring, little Curled.
  - Stretch goal, only if the plan finds it feasible: B (four fingers
    Extended and Together, thumb Curled) vs. 5 (four fingers Extended and
    Apart, thumb Extended).
- Explicit pose priority order for overlapping poses. Most specific match
  wins; Open and Fist are checked last. The order must be written down in
  code comments and covered by tests.
- Debug line additions: show the raw spread value for each evaluated pair
  next to its state, e.g. "Idx-Mid Apart (0.42)", and "n/a" when invalid.
  Keep the existing five finger states and the raw thumb ratio.
- Visualization stays at five fingers (see v0.075 notes below).

Out of scope (do NOT build yet):
- Thumbs-up, world-space direction, XR origin transforms, any orientation
  logic. Those are v0.2b.
- Poses distinguished only by orientation or subtle thumb placement
  (H vs. U, K vs. V, G, Q, and the A/E/S/T/M/N group).
- Dynamic signs (J, Z), two-hand signs, motion, sequences of frames.
- Recording, databases, ScriptableObjects, normalization pipeline,
  recognition state machine, lessons.

Constraints:
- Open, Fist, and the thumb state must behave exactly as before. All
  existing EditMode tests must pass UNEDITED. Verify with git diff that
  no existing test method changed.
- The bodies of Classify, Tally, FingerCurlDegrees, ClassifyThumb, and
  ThumbTipDistanceRatio must not change. New pose resolution goes in new
  code. Additive changes to the HandPose enum are allowed; do not renumber
  existing values.
- Open continues to mean "four non-thumb fingers Extended, thumb ignored",
  not "5". Open is the lowest-priority fallback.
- Spread is only evaluated for pairs where both fingers are Extended.
  A Curled or Unknown finger makes that pair's spread Unknown, never a
  guess.
- New thresholds are exposed in HandPoseClassifierSettings under a
  [Header("Spread")]. Spread thresholds are ratios (unitless); a larger
  ratio means more spread. Add tooltips stating this.
- Core logic stays pure C# (no MonoBehaviour), testable without a headset.
- Existing scene values (60/160 curl thresholds and the tuned thumb
  values) must not be overwritten. New fields take their script defaults
  until the teammate tunes them in the Inspector.

Phases (one at a time; stop and report after each):
1. Logic and tests only (settings, spread measure, pose resolution, new
   EditMode tests). Verified on Mac. Commit.
2. Adapter/display changes (debug line with raw spread values). Verified
   on the headset by the teammate via Quest Link. Commit.

Test requirements:
- One test per new pose with a clean fake hand, plus a negative case for
  each (e.g. L with the thumb curled must not return L).
- V vs. U boundary: spread just on each side of both thresholds, and in
  the Unknown band.
- Spread invariance: rotated, translated, and scaled hands give the same
  state.
- Invalid cases: missing joints, NaN, coincident joints, untracked hand,
  reversed or NaN spread thresholds return Unknown.
- Priority tests: poses that satisfy more than one rule resolve to the
  documented winner (e.g. Pointing vs. L, B vs. Open).
- Regression: Fist and Open results unchanged for every new fake hand.

Done when:
- All old and new EditMode tests pass with no red Console errors.
- Teammate confirms on the headset: index and middle together reads U,
  index and middle apart reads V, Pointing/L/Y read correctly, and Open
  and Fist still work.
- Spread thresholds tuned from the raw on-screen values, saved in the
  scene and committed. Defaults in HandPoseClassifierSettings.cs updated
  to match, or the tuned values recorded in this file.



## v0.075 spec (Thumb tracking only)
Goal: the classifier reports a per-finger state (Extended, Curled, or
Unknown) for the thumb, alongside the existing four fingers.

In scope:
- Add thumb joints to the adapter's joint data (ThumbMetacarpal,
  ThumbProximal, ThumbDistal, ThumbTip). Thumb has no Intermediate joint.
- Compute a thumb state with thresholds exposed in the settings class.
- Missing or invalid thumb data returns Unknown, never a guess.
- EditMode tests: thumb extended, curled, missing.
- Optional debug text showing all five finger states.

Out of scope (do NOT build yet):
- New poses (thumbs-up, pointing, ASL letters). Those are v0.2.
- Orientation or world-space direction logic.
- Recording, databases, normalization, lessons.

Constraints:
- Open and Fist behavior must not change. Existing tests must pass unchanged.
- Open and Fist continue to ignore the thumb.
- Core logic stays a pure C# function, testable without a headset.

Done when:
- All old and new EditMode tests pass.
- Friend confirms on the headset that the thumb reads Extended when out,
  Curled when tucked, and Unknown when hidden, and Open/Fist still work.



## v0.1 spec (COMPLETE, kept for reference and constraints only)
## Objective
Build a minimal Unity application that recognizes two static hand poses using Meta Quest 2 hand tracking:

- Fist
- Open Hand

The application should display the currently recognized pose inside the VR environment.

## Technical Requirements
- Use Unity, OpenXR, and XR Hands with a compatible Quest 2 hand-tracking configuration.
- Use built-in headset hand tracking, not OpenCV or an external webcam.
- Do not implement machine learning.
- Do not implement a recording system, database, ScriptableObjects, normalization pipeline, or ASL lesson system.
- Keep the architecture simple and modular.

## Required Scripts

**QuestHandAdapter.cs**
- Access hand-tracking data through Unity's XR Hands API.
- Read the relevant finger joint positions.
- Check tracking validity before using joint data.
- Expose the information needed by the classifier.

**HandPoseClassifier.cs**
- Analyze finger joint angles.
- Determine whether four non-thumb fingers are extended or curled.
- Return `HandPose.Open`, `HandPose.Fist`, or `HandPose.Unknown`.
- Expose classification thresholds for tuning in the Unity Inspector.
- Avoid classifying missing or invalid tracking data as a valid gesture.

**HandPoseDisplay.cs**
- Read the current classification.
- Display `OPEN`, `FIST`, or `UNKNOWN` through TextMeshPro.
- Update the displayed text when the classification changes.

## Implementation Rules
- Use the installed Unity XR Hands package's actual API.
- Do not invent nonexistent Unity methods.
- Keep classes independent where practical.
- Add comments explaining important Unity and C# concepts.
- Provide exact Unity Editor setup instructions, including which GameObjects need scripts and which Inspector references must be assigned.
- Start with one hand; allow future extension to two hands.

## Definition of Done
1. The Quest 2 application runs successfully.
2. The system receives valid hand-tracking data.
3. Making a fist displays FIST.
4. Extending the hand displays OPEN.
5. Partial poses or tracking loss display UNKNOWN.
6. No unnecessary recognition architecture is implemented.



## Roadmap
0.1   Fist vs Open (done)
0.075 Thumb tracking (done)
0.2a  Finger spread + Pointing, V, U, Y, L (current)
0.2b  Thumbs-up (world-space thumb direction; new orientation layer)
0.3   Pose recording (HandFrame, HandPoseRecorder, HandPoseData)
0.4   Template matching (normalization, pose database, shape recognition)
0.5   ASL learning environment (lessons, feedback, motion, two hands)

## Completed versions
### v0.1 (Fist vs Open): COMPLETE
Verified on the headset via Quest Link. Tuned curl thresholds are the
current defaults (extendedMaxCurlDegrees 60, curledMinCurlDegrees 160
in the scene).

### v0.075 (Thumb tracking): COMPLETE
- Thumb state (Extended / Curled / Unknown) from the ratio
  |thumb tip - index proximal| / thumb length. Larger ratio means more
  extended. Chosen over a single joint angle because a tucked thumb moves
  mostly at its base joint, which bend angles barely register; XR Hands'
  own gesture math reached the same conclusion for the thumb.
- Thumb has no Intermediate joint: ThumbMetacarpal, ThumbProximal,
  ThumbDistal, ThumbTip only, held in a separate ThumbJoints struct.
- Thumb state depends on the index data; invalid index makes the thumb
  Unknown.
- Thresholds: thumbExtendedMinTipDistanceRatio [TUNED VALUE, default 0.60],
  thumbCurledMaxTipDistanceRatio [TUNED VALUE, default 0.45].
- Open and Fist ignore the thumb. trackingState and the four-finger hint
  logic are unchanged.
- Headset finding: [FILL IN: what the thumb reads when hidden behind the
  hand, Unknown vs. a predicted value].
- Visualization fix: the hand visualizer never called DrawFinger() for the
  thumb, so only four fingers rendered. Fixed by editing the sample
  scripts under Assets/Samples/. Note: re-importing the XR Hands sample
  overwrites these edits. [FILL IN: moved to Assets/Scripts/Debug/ or not.]
