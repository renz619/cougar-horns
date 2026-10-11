# Hand gesture handoff: J/Z motion and future two-hand signs

Prepared October 11, 2026. Paths are relative to `codeRedUnity/`.

**Status: software phases A-E implemented October 11, 2026; phase F device/ASL validation pending.** The opt-in J/Z recorder, complete-sequence recognizer, runtime lifecycle, Editor review/persistence and separate demo are delivered. Left-hand and two-hand evaluators remain future work. See [HAND_GESTURE_MOTION_SETUP.md](HAND_GESTURE_MOTION_SETUP.md) for exact wiring, settings, test evidence and limitations, and section 13 for the completion report. The specification below remains the acceptance contract; implementation does not imply headset or ASL accuracy validation.

## 1. What the developer should build

Keep the current static hand-pose system working. Add an opt-in system that recognizes complete ASL J and Z movements using the existing Quest hand tracking. Design the new gesture contracts so a later developer can add a left-hand source, static two-hand gestures, and moving two-hand gestures such as COFFEE and TEA without replacing the J/Z data model.

In plain language: the existing recognizer checks a photograph of a hand. The new motion recognizer checks a short movie. Future two-hand recognition must check both hands together, including their arrangement and timing.

Hand count and movement are separate choices:

| Gesture category | What must match | Delivery |
| --- | --- | --- |
| One hand, static | One handshape, plus configured orientation rules | Existing behavior remains available |
| One hand, moving | Handshape/phase rules plus a complete ordered movement | Implement J and Z first |
| Two hands, static | Both handshapes, their relative position/orientation, and a stable hold | Future after left-hand capture |
| Two hands, moving | Both handshapes/phase rules, arrangement, and synchronized movement | Future COFFEE/TEA |

Do not model two-hand support as a feature exclusive to motion. A static two-hand gesture must work with zero required trajectory and must not need DTW.

### Scope and document precedence

Read [CLAUDE.md](CLAUDE.md), [HAND_POSE_FOUNDATION_SETUP.md](HAND_POSE_FOUNDATION_SETUP.md), [HAND_POSE_RECOGNITION_HANDOFF.md](HAND_POSE_RECOGNITION_HANDOFF.md), and [HAND_POSE_RECOGNITION_SETUP.md](HAND_POSE_RECOGNITION_SETUP.md). This document defines the proposed next milestone and supersedes their historical exclusions of motion only for J/Z and the explicitly specified extension contracts. Hardware two-hand recognition remains future work.

Preserve existing classifier behavior, serialized references, enum values, `.meta` GUIDs, schema/layout/normalization versions, tests, and tuned scene settings. Do not migrate static assets merely to add motion. Keep math, matching, state transitions, and recording policy in plain C# with injected data and clocks. Unity components connect those functions to tracking, scenes, and UI; Editor code handles asset persistence. Verify new XR API calls against installed package source.

This is hand tracking for the listed learning activities, not a specification for general ASL translation, body-location recognition, or facial/nonmanual features.

## 2. Current project facts

| Existing file/API | Relevant behavior |
| --- | --- |
| `QuestHandAdapter.cs` | Reads one selected hand on XR Hands `Dynamic` updates; exposes immutable raw frames and legacy data. It already has a left/right selection, but the existing demo is right-handed. |
| `IHandFrameSource` | Exposes one physical hand through `TryGetLatestFrame(out HandFrame)`; a returned frame can explicitly report tracking loss. |
| `HandFrame` | Owns 26 ordered joints, positions/optional rotations, `TimestampSeconds`, `SequenceId`, physical `HandSide`, and space/version metadata. Raw positions are in tracking-space meters. |
| `HandPoseNormalizer` | Subtracts the current wrist, removes the current hand basis, and scales by current palm width on every frame. Useful for shape; it removes rigid hand translation/rotation needed for motion. |
| `PoseSample`, `HandPoseData`, `PoseDatabase` | Schema 1 stores alternative canonical static samples. It does not define complete gesture clips. |
| `ShapeRecognizer` | Compares one canonical frame against static samples. It does not compare ordered sequences or use joint rotations. |
| `PoseRecordingSession` | Records independent canonical shape samples, with a default 0.1-second interval. Reuse its ownership/review ideas, not its static sample semantics. |
| `RecognitionSession` | Applies static evaluation interval, dwell, and cooldown. Its default 0.1-second evaluation interval must not become the motion acquisition interval. |

The earlier recognition setup reports 392 passing tests and pending hardware verification. The baseline rerun before this implementation produced **392 total: 391 passed, 1 failed**. The existing recognition-demo test expects LegacyRules while the current saved scene selects Template. This pre-existing mismatch is preserved and documented, not counted as a motion regression; see [baseline XML](Verification/HandGestureMotion/baseline.xml).

The installed `Library/PackageCache/com.unity.xr.hands/Runtime/XRHandSubsystem.cs` exposes both hands and documents `Dynamic` for game logic and `BeforeRender` for visuals. Keep that distinction. See the [Unity XRHandSubsystem reference](https://docs.unity3d.com/Packages/com.unity.xr.hands@1.5/api/UnityEngine.XR.Hands.XRHandSubsystem.html).

## 3. Architecture: one gesture layer, separate shape and movement

```text
Tracking source -> raw observation -> validity/freshness checks
                                |
              +-----------------+-------------------+
              |                                     |
   Per-hand canonical shape                 Shared spatial features
   (existing normalization)                 (retain placement/rotation)
              |                                     |
              +-----------------+-------------------+
                                |
                   Static or motion evaluator
                                |
                  Confirmation/rearm/event policy
                                |
                     Lesson/display integration
```

For static gestures, evaluate shape and configured spatial constraints, then use distinct-acquisition hold confirmation. For motion gestures, additionally collect a bounded history and compare a complete ordered sequence. Keep legacy recognition selectable; use string gesture IDs such as `asl.letter.z`, rather than adding every sign to `HandPose`.

### New contracts to introduce with J/Z

Names below are proposed project APIs, not existing Unity APIs. Equivalent names are acceptable when their contracts are retained.

| Contract | Required responsibility |
| --- | --- |
| `GestureDefinition` | Common ID/name, `Static` or `Motion`, required hand roles, side/dominance policy, shape/phase rules, optional spatial constraints, and settings. |
| `GestureObservation` | Owned raw observations with optional Left/Right slots, common observation time, source/session identity, coordinate epoch, and distinct-acquisition identity. J/Z initially populates only the selected side. |
| `GestureClip` / `GestureMotionSample` | One complete ordered performance; relative timestamps, available-hand masks, raw hand evidence or sufficient versioned derived evidence, frozen reference metadata, and provenance. Multiple clips are alternative performances. |
| `GestureStaticSample` | Extension contract for a simultaneous static arrangement: canonical sample per required role plus shared spatial features. Implement its evaluator later, without inventing a fake motion clip. |
| `GestureData` / `GestureCatalogSnapshot` | New versioned assets and validated immutable runtime definitions/examples; static gesture entries can reference existing `HandPoseData`. Unsupported categories are unavailable, never silently approximated. |
| `GestureRecordingSession` | Bounded ordered capture, explicit Begin/Stop/Cancel, owned review payload, rejection/completion reasons, and frozen recording settings. |
| `GestureFeatureExtractor` | Pure extraction of shapes, trajectory, wrist orientation, and later hand-to-hand features. |
| `MotionGestureRecognizer` | Pure complete-sequence scoring and coverage checks; no scene lookup or events. |
| `GestureRecognitionSession` | Motion boundaries, lifecycle/reset, completion, cooldown/rearm, and one confirmation event per performance. |
| `GestureRecognitionResult` | Status, matched ID, separate diagnostic candidate, score availability, clip/phase coverage, interval, physical participating sides, role mapping, and failure reason. |

Freeze the optional two-hand slots, role semantics, spatial-feature extension, and category separation now. Build only the one-hand producer and J/Z consumers now. Future two-hand definitions may be authored as unsupported entries, but cannot be reported as recognized before their evaluator/source exists.

Keep `HandFrame`, `HandSide`, `HandFrameSpace`, and static schema 1 unchanged. In particular, do not add `Both` to `HandSide`: each `HandFrame` still represents one physical hand. Shared gesture coordinates belong to a new feature contract, not falsely labeled `CanonicalHandSpace`.

### Physical side versus signing role

`Left` and `Right` describe physical hands. `Dominant` and `NonDominant` describe their roles in a sign. Store explicit dominance in user/recording configuration; do not infer it from which hand moves. Right-handed J/Z is the initial validated configuration. The active dominant hand must have an available source before recognition can run.

For future asymmetric signs, map roles according to dominance. For symmetric signs, define whether role exchange is permitted. Record actual physical side and role mapping as provenance. Initially store separate left/right examples; mirroring must be a later explicit, tested feature, including geometry, orientation, and role handling. Never flip the old canonical normalization implicitly.

## 4. Coordinates: preserve the evidence

### J/Z reference frame

At recording/candidate start, freeze an orthonormal basis expressed in tracking space:

- Up follows the calibrated rig/world vertical; forward follows the headset's horizontal forward at start; right completes the basis.
- Supply the basis through a thin Unity integration adapter. The pure extractor receives numerical vectors/rotation and does not query a camera.
- Validate metadata, finite values, positive uniform rig scale assumptions, and a nondegenerate horizontal forward. Missing calibration is an explicit unavailable state.
- Freeze the initial palm-width scale. Do not change it as the hand rotates or joints jitter.

For a one-hand fingertip path:

```text
tipPath(t) = inverse(startBasis) * (rawTip(t) - rawTip(start)) / startPalmWidth
```

Also retain the wrist path in that fixed basis, and raw wrist orientation converted to that basis. If a rotation-change feature is used, explicitly define its multiplication order and feature version. Avoid Euler-angle subtraction; compare quaternion orientation using shortest angular distance and handle equivalent q/-q representations.

Do not subtract the moving wrist from the fingertip for the main path. Do not update the basis to follow the hand or head. Head movement after start must not redraw the trajectory's axes. Retain 3D evidence even if a first visualization emphasizes the horizontal/vertical plane.

`HandFrame.TrackingSpaceToWorld` contains a rotation, not a complete position transform or canonical-to-world transform. Keep raw positional operations in their common tracking space. If world positions are needed later, capture the appropriate full transform separately and convert exactly once.

### Preserve two-hand arrangement from the start

When both hands exist, use one shared origin, basis, and scale for their spatial features. Subtracting each hand's own starting point would erase their initial spacing. Independently canonicalizing both hands is allowed for shape, but not as the only evidence for their relationship.

Reserve features for wrist/palm displacement between roles, selected cross-hand joint distances, and relative wrist/palm orientation. For example:

```text
relativePosition(t) = inverse(sharedBasis) * (dominantPalm(t) - nonDominantPalm(t)) / sharedScale
relativeRotation(t) = inverse(nonDominantRotation(t)) * dominantRotation(t)
```

Some later signs need a path expressed in the supporting hand's current local basis; allow a versioned sign-specific feature extractor. Retain both hands' raw evidence/common-space paths so this feature does not hide movement of the supporting hand. A stationary-hand constraint should explicitly distinguish motion relative to the body/reference frame from motion relative to the other hand.

Static two-hand samples store the same instantaneous relationship features. Their reference is stable through the hold, so drifting hand placement cannot be normalized away into an apparent match.

Reset candidates and stop recordings on coordinate-epoch changes: recentering, teleport/rig changes, tracking-space discontinuity, or source replacement. Do not join incompatible coordinates into one trail. If the integration cannot detect a discontinuity reliably, provide an explicit reset hook and document the supported setup.

## 5. J and Z recognition rules

| Letter | Initial shape gate | Ordered motion evidence |
| --- | --- | --- |
| Z | Index extended; other non-thumb fingers curled; thumb placement validated against the chosen recordings | `IndexTip`: top stroke, diagonal reversal/downward stroke, bottom stroke, in the configured signing direction |
| J | I-like shape: little finger extended, other non-thumb fingers curled, thumb configuration validated from recordings | `LittleTip`: downward portion and curved finish, retaining wrist rotation evidence |

Use [ASL fingerspelling guidance](https://www.lifeprint.com/asl101/pages-layout/fingerspelling.htm), [J demonstration](https://www.lifeprint.com/asl101/pages-signs/j/j.htm), and [Z demonstration](https://www.lifeprint.com/asl101/pages-signs/z/z.htm) when selecting real examples. Z's left-handed movement is mirrored. Do not hard-code screen directions from a mirrored video.

The existing `Pointing` rule is a useful Z gate, not proof of Z. Add a dedicated I-like candidate gate for J without altering protected classifier bodies or requiring the legacy enum to contain I. Keep gates candidate-specific: the globally winning static template may not be the shape needed by a particular gesture. Use a dedicated predicate or score the configured gate templates directly.

Allow natural wrist rotation and mild finger variation during J. Shape requirements can differ by phase; do not demand an identical canonical sample throughout the clip. However, unknown/missing required evidence cannot be treated as matching a phase.

### Full-sequence matching with DTW

Use bounded dynamic time warping (DTW) against recorded complete examples. It aligns ordered sequences that were signed at different speeds. For the first J/Z implementation:

1. Smooth valid points lightly using a timestamp-aware filter; reject implausible jumps rather than smoothing through tracking loss. Apply identical versioned preprocessing to live and stored clips.
2. Resample valid complete sequences to a fixed bounded count while retaining original duration and timestamps. Never interpolate across invalid tracking gaps.
3. Compute a weighted local distance from configured path/orientation features. Normalize units deliberately: palm widths and angular distances require separately documented weights/scales.
4. Use endpoint-constrained DTW: the alignment starts at both sequences' first points and ends at both sequences' last points. Bound the warping band and stretching so tiny fragments cannot match an entire sign.
5. Normalize accumulated cost by alignment-path length. Use finite double accumulation, bounded working arrays, and deterministic ID/sample tie handling. Lower error means closer; it is not a probability.
6. Require all independent checks: valid shape phases, minimum excursion/path length, allowed original duration, expected stroke/phase coverage, and score <= the candidate's threshold.

Motion completion is separate from a good partial score. An unfinished top stroke cannot confirm Z. Prototype completion using an end pause after all required phases; reject long timeouts, buffer limits, and shape loss as completion unless a full valid gesture was already explicitly completed. Preserve a short bounded pre-movement history while Armed so onset detection does not discard the first stroke.

Choose the lowest eligible full-gesture error, break ties deterministically, and check that winner's threshold; do not switch to a worse candidate merely because its threshold is looser. If calibrated ambiguity rejection is later added, specify and test it separately. Expose the diagnostic candidate and rejection reason.

### Starter budgets, subject to measurement

Use a two-second candidate/recording window and a 256-frame cap as initial configurable prototype budgets, with a separate bounded attempt count. These are engineering seeds, not established ASL timing requirements. Record all distinct Dynamic acquisitions; do not throttle capture to the static 10 Hz evaluation policy. If actual acquisition rate exceeds the cap, report the limit and revise/resample deliberately. Never silently truncate a training clip into a valid example.

A provisional 64-point resampled comparison is reasonable for bounded experiments. Minimum movement, allowed duration, pause timing, smoothing, DTW band, maximum gap, and score thresholds require recordings and headset tuning. Validate all numeric settings and record exact final values; do not invent accuracy or confidence claims from these defaults.

## 6. Recording and editor review

First build manual boundaries: choose a letter and active side/dominance, Begin, perform the whole sign, Stop, review, save. Automatic gesture boundaries come after replay matching works.

Freeze definition ID/version, feature selection/weights, source identities, side/roles, reference/calibration, and limits at Begin. Accept monotonic distinct acquisitions only; repeated reads of one `SequenceId` are not new evidence. Track per-source identity because existing left and right sequence numbers cannot be compared globally. Reject future/stale/untracked/wrong-side/missing-required/nonfinite frames with reasons. Tracking loss or a missing required acquisition invalidates the current motion clip; do not bridge it with zero-filled data.

Store relative time from clip start, original acquisition provenance, calibration and preprocessing versions, and enough owned joint evidence to reproduce shape and spatial checks. Mutable caller arrays must not alter accepted/reviewed/runtime data. Bound duration, frames, attempts, clip count, and catalog size. Cancel discards pending data; Stop yields a review payload that may be invalid and unsavable.

The editor review must show the complete trail, start/end, duration, participating roles, validity gaps, frame counts, phase coverage, and reasons. Allow trimming only as an explicit operation followed by full revalidation. Save examples as complete clips, not a bag of frames. Begin with a few varied real performances and expand according to measured errors; keep synthetic fixtures clearly labeled as tests.

Use new motion asset folders, separate from `Assets/HandPoses/Synthetic`. Example destination: `Assets/HandGestures/Recorded/`. Provide explicit create/append/replace, Undo, prospective validation, atomic catalog rebuild, save/reimport/reload checks, and pending-review retention through Play Mode/domain transitions. Explain when pending review is lost on editor shutdown. Do not auto-save or overwrite reviewed assets.

## 7. Runtime lifecycle and lesson behavior

```text
Idle -> Armed -> Tracking -> Completed -> AwaitRearm -> Idle
                   |
                 Rejected -> AwaitRearm
```

- Idle waits for valid required sources and a starting shape.
- Armed freezes the reference and retains a bounded onset history; actual movement starts Tracking.
- Tracking accumulates valid distinct acquisitions and phase progress; a terminal segment/pause triggers complete-clip evaluation.
- Completed emits one `GestureConfirmed` event, then moves to AwaitRearm.
- AwaitRearm requires a configurable release/new-gesture boundary plus cooldown. A cooldown expiring alone does not replay the old clip. Rejecting a partial attempt must also avoid immediately restarting from its suffix.

An injected clock keeps bounds advancing even when no frames arrive. Invalid clocks, stale input, loss of any required source, missing required joints, side/dominance changes, disable/re-enable, catalog/feature changes, and coordinate resets clear candidate/confirmation state. Report why; leave no stale success label. A missing unused hand does not invalidate a one-hand sign.

Keep geometric observations separate from confirmed events. When motion is enabled in a lesson, reserve a plausible I/pointing candidate briefly so a static event does not commit the start of J/Z prematurely. On valid completion, motion wins for that performance. If the hand stays still through the documented reservation/hold period, the static path may confirm where appropriate. Define this in the new arbitration layer, not by changing legacy defaults. A lesson limited to J/Z can simply enable only those gesture candidates.

Display selected letter, state, fingertip trail, duration, phase/stroke progress, score availability, best example, and rejection reason in developer debug UI. Product feedback should use plain messages such as "Finish the bottom stroke" or "Hand tracking lost." Never present an uncalibrated error score as a percent correct.

## 8. Future left-hand capture and synchronization

When the left hand is implemented, introduce a coherent two-hand producer inside the tracking integration boundary. Prefer reading both physical hands from the same `Dynamic` callback and publishing one owned observation with common time/acquisition ID, per-hand validity, and a coordinate epoch. It can expose compatible per-hand views so existing consumers keep working.

Do not pair an old left frame with a fresh right frame just because both are individually within a broad freshness limit. Never equate left/right `SequenceId` values. If an interim adapter aggregates separate sources, configure maximum timestamp skew, accept only fresh per-hand evidence, track each source's advancement, and reject unsupported/skewed pairs. The later coherent producer removes this pairing ambiguity.

Two-hand dwell needs distinct coherent paired observations across acquisition time; rereading the same pair does not complete a hold. Loss of either required hand clears two-hand candidates immediately. One-hand recognition can continue on its remaining valid source. Hand-to-hand occlusion is a device-validation requirement, not a reason to guess hidden joints or treat unavailable data as an open/fist pose.

## 9. Future static two-hand gestures

Implement `TwoHandStaticRecognizer` using the already reserved common definition/observation/static-sample contracts. A valid match requires all configured constraints together:

1. Both physical hands are tracked and temporally coherent.
2. Each role matches its required handshape template/rule.
3. Shared spatial features match: relative placement, orientation, separation, and selected joint proximity as appropriate.
4. The combined arrangement remains matched for the configured dwell, supported by distinct paired acquisitions.

Examples of configuration capabilities: one hand above the other, palms facing each other, fingertips near a chosen joint, allowed spacing range, optional proximity/contact approximation, and symmetric role exchange. Tracked proximity is not a physical contact sensor; calibrate tolerances against Quest data.

Do not AND two unrelated `PoseConfirmed` events together. They may come from different times and their canonical shapes discard hand placement. Score the current coherent pair directly, then confirm the combined gesture. Its output is one gesture ID/event, not two letter events.

Add a synthetic static two-hand arrangement fixture for deterministic tests and a separately reviewed real sign/example for hardware validation. This future phase must work without moving either hand, without recording a trajectory, and without calling DTW.

## 10. Future COFFEE and TEA motion

These are future acceptance targets, not templates provided by this handoff.

- COFFEE should evaluate the selected handshapes, stacked arrangement, support-hand stability, and the active hand's grinding/rotating movement relative to the supporting hand. The [Lifeprint COFFEE demonstration](https://www.lifeprint.com/asl101/pages-signs/c/coffee.htm) describes a stable lower hand and movement of the upper hand.
- TEA uses an F handshape on the dominant hand and a C or O shape on the nondominant hand according to [Lifeprint's TEA reference](https://www.lifeprint.com/asl101/pages-signs/t/tea.htm). Select and record a demonstrated variant; derive its movement phases and hand-to-hand features from that performance rather than inventing a universal trajectory from the word "tea."

Validate each intended variant with an ASL-fluent reviewer before labeling real training examples. Record the variant, dominance, demonstrated movement, repetition policy, required features, and reviewer status. The lower hand being stationary still matters: it must remain valid and correctly shaped/placed throughout the relevant phases.

For moving two-hand signs, form one time-aligned feature vector containing both hands and their relationships. Use one shared DTW alignment for the entire vector. Independently warping/matching each hand could accept two movements that never occurred together. Weight support-hand and cross-hand evidence explicitly; do not allow zero weights to silently remove a required hand. Version any additional orientation-sensitive feature extractor without changing old static shape data.

## 11. Implementation order and concrete deliverables

| Phase | Work | Evidence required before proceeding |
| --- | --- | --- |
| A: additive contracts | Definition categories, roles/side mapping, optional hand slots, owned clips, calibration/epoch metadata, validation/snapshots, and explicit unsupported future categories | Pure ownership/version/validation tests; unchanged static baseline |
| B: manual recorder/replay | One-hand recording, complete trail visualization, review/save/reload, preprocessed replay | Recorded complete clips survive reload; invalid/incomplete clips are unsavable with reasons |
| C: Z matching | Dedicated shape gate, fixed-frame path, DTW/full-stroke checks, deterministic diagnostic result | Positive/negative synthetic and replay tests; no partial-path acceptance |
| D: J matching | I-like gate, pinky path, wrist rotation, phase variation | J versus held I and incomplete/reversed paths; valid wrist-driven examples |
| E: runtime integration | Automatic boundaries, reset/rearm, one event, optional controller/display/demo, static/motion arbitration | Lifecycle/integration/regression tests and developer setup instructions |
| F: hardware tuning | Quest Link capture, real examples, threshold selection, held-out replay and live evaluation | Actual device logs, per-sign results, saved settings, measured performance/limitations |
| Future G: both hands | Coherent producer, left source, dominant-role mapping, skew/loss policies | Paired acquisition/synchronization and device occlusion checks |
| Future H: static two hands | Static pair assets/evaluator and distinct-pair dwell | Static arrangement works with no motion/DTW; wrong arrangement and stale pairs fail |
| Future I: moving two hands | COFFEE/TEA reviewed clips and relationship features, one shared sequence alignment | Whole-pair validation across dominance, movement, placement, and support-hand failures |

Suggested new runtime files under `Assets/Scripts/HandPose/`: `GestureDefinition.cs`, `GestureObservation.cs`, `GestureClip.cs`, `GestureData.cs`, `GestureCatalogSnapshot.cs`, `GestureFeatureExtractor.cs`, `GestureRecordingSession.cs`, `MotionGestureRecognizer.cs`, `GestureRecognitionResult.cs`, `GestureRecognitionSession.cs`, and thin controller/recorder/debug components. Add Editor recorder/persistence files under the existing `Editor/` assembly and additive tests under `Assets/Tests/EditMode/`. Consolidate small types where that improves clarity; avoid creating empty future evaluator classes.

Create a separate opt-in `HandGestureMotionDemo` with the existing right-hand source, explicit calibrated reference provider, gesture catalog, recorder/controller, and trail/debug display. Supply exact Inspector assignments and setup steps, and generate new Unity asset `.meta` files through Unity. Preserve the existing demos and their saved tuning. Do not introduce packages or train a neural model for this milestone.

## 12. Verification and acceptance checklist

### Deterministic software checks for J/Z

- [ ] Original static/legacy tests pass unedited; old assets load and old scenes retain references/tuning. **391/392 pass; the pre-existing saved-scene/default mismatch remains. Original scenes, static assets and tests are unedited.**
- [x] New data is owned, bounded, versioned, and validated; malformed numeric settings/data cannot match.
- [x] Ordered clips preserve timestamps/rotations; repeated reads, backwards sequence/time, future/stale frames, and source replacement are handled explicitly.
- [x] A hand-computed small DTW example verifies cost, endpoint constraints, normalization, and deterministic ties.
- [x] Valid synthetic J/Z examples match at different supported speeds, start positions, and positive hand scales; real-ASL generalization awaits phase F.
- [x] Camera/head movement after start does not alter the fixed axes; invalid calibration and coordinate resets reject cleanly in software tests. Provider recentering requires the documented explicit reset hook.
- [x] Held pointing/I shapes, jitter, wrong handshape, straight swipes, reversed/wrong-order strokes, incomplete paths, excessive duration, and implausible jumps are covered by deterministic rejection checks.
- [x] Tracking gaps/missing required joints or rotations invalidate required evidence; no bridging or synthetic zeros create a match.
- [x] Full motion confirmation produces one event; holding the end pose, cooldown expiry, and rereading the buffer do not replay it. A new valid performance can confirm after rearm.
- [x] Partial motion does not emit a static letter in the delivered JZOnly lesson mode. Mixed static/motion lesson arbitration is not delivered.
- [x] Begin/Stop/Cancel, invalid review, save/append/replace/Undo, Play Mode/domain transitions, catalog reload, and disabled/destroyed components are exercised by automated tests.
- [x] Pure math/replay and component lifecycle tests are distinguished from actual hardware/ASL accuracy evidence.

### Future two-hand contract and acceptance checks

Add pure contract tests now where relevant; execute real evaluator/device tests in the future phases.

- [x] Current one-hand J/Z assets round-trip with an explicitly absent left slot and require no left source. Future capture must preserve this tested schema contract.
- [x] A two-hand-required entry reports unavailable/unsupported when its source/evaluator is absent; it never degrades into a one-hand match.
- [x] Physical Left/Right stays distinct from dominant/nondominant roles in the data contract. Left-dominant evaluator/device acceptance remains future work.
- [ ] Same-shaped hands in the wrong arrangement, stale/skewed pairs, duplicate pairs, and loss of either required hand cannot confirm a static two-hand gesture.
- [ ] Static two-hand gestures confirm from stationary arrangements with distinct-pair dwell and no DTW.
- [ ] Moving two-hand gestures require synchronized phases using one alignment; independently correct but mistimed hand movements fail.
- [ ] COFFEE/TEA reject missing/wrong support hand, wrong arrangement, static handshape alone, and incomplete movement; selected variants and repetition policies are documented.
- [ ] Common translation does not erase relative layout; individual-hand translations do change the arrangement score. Global motion and support-relative motion remain separately available where required.

### Device evidence and performance

Record real J/Z with varied speeds and comfortable start positions. Separate training, threshold tuning, and held-out evaluation by whole performance; do not split neighboring frames from one clip across those sets. Use additional signers when available and report their coverage honestly. Track per-sign accepted/rejected counts, false activations on negatives/transitions, confusion, and confirmation latency. For future two-hand signs, include occlusion and both supported dominance configurations.

Measure capture, extraction, matching, and visualization separately on the target headset with a stated catalog/clip/feature size. Report allocations, bounded buffer/DTW memory, and match latency. Reuse arrays; avoid per-candidate cloning, asset rebuilds, and LINQ allocations in hot paths. Do not claim zero allocations for the entire pipeline: the current adapter constructs owned frames. Optimize only from measured evidence, preserving ownership and compatibility.

## 13. Implementation completion report

| Item | Delivered result / remaining evidence |
| --- | --- |
| Delivered phases | A-E software: additive contracts; manual complete-clip capture/review/persistence; fixed-reference J/Z extraction and bounded DTW; automatic boundaries, release/cooldown and one event; opt-in demo/debug UI. |
| Deferred phases | F requires actual Quest Link and ASL-reviewed recordings. Future G/H/I coherent paired capture, static two-hand evaluation, and COFFEE/TEA shared-alignment evaluation remain unsupported. |
| Changed files/responsibilities | Full inventory in [setup](HAND_GESTURE_MOTION_SETUP.md). Runtime code stays under `Assets/Scripts/HandPose`, Editor code in its existing Editor assembly, tests in the existing EditMode assembly. The only modified pre-existing source is the additive `IHandFrameStream` event in QuestHandAdapter. No package, existing test, static schema, enum, asset GUID or original scene changes. |
| Versions/compatibility | Gesture schema/feature/preprocessing/calibration versions 1. Owned raw 26-joint evidence, optional physical slots, explicit role mapping and simultaneous static sample contract. Old static schema/layout/normalization remain unchanged. |
| Unity setup/reference | Delivered `Assets/Scenes/HandGestureMotionDemo.unity`, Motion Disabled by default. Existing right-hand adapter; Camera Offset tracking space; Main Camera heading; independent motion label/trail; empty recorded MotionCatalog. Recenter discontinuities require `ResetCoordinates()`. Exact workflow in setup. |
| Automated evidence | Final: **461 tests, 460 passed, 1 pre-existing failure; all 69 new gesture tests passed**. Baseline and final XML/logs in `Verification/HandGestureMotion/`; exact command and profiling results in setup. Existing baseline scene-default failure remains disclosed. Tests include actual Play Mode/domain transitions and Dynamic callback integration; they are not headset tests. |
| Quest Link checks | Not run. Baseline legacy behavior, live capture, coordinate resets, loss/recovery, negatives, confirmations and target performance all require device verification. |
| Recording/reviewer provenance | No real J/Z training corpus or ASL reviewer approval supplied. Fixtures are explicitly synthetic and confined to tests; delivered catalog is empty. Record intended variants through the review workflow before enabling recognition. |
| Thresholds/settings | Exact provisional values and hard caps are listed in setup and frozen in each saved definition/clip. They are engineering defaults, not final calibrated ASL thresholds. |
| Held-out accuracy/false activations/latency | Not measured. Split by whole performance; record signer coverage, per-sign counts, negative/transitional false activations and latency during F. |
| CPU/allocations/memory | Automated Editor-host profiling separates observation construction, extraction, complete replay, recording admission and debug text. Reused DTW arrays: 7,680 bytes default; 202,752 bytes at hard limits. Headset CPU/allocation bytes/rendering cost remain unmeasured. |
| Limitations | Right-dominant J/Z only; provisional shape/phase/direction/thumb thresholds; optional latest-frame-only sources cannot promise every acquisition. QuestHandAdapter uses the new Dynamic stream. Review survives domain/Play Mode transitions while its window is open, not editor shutdown. No general ASL translation or two-hand recognition. |
| Next concrete task | ASL-fluent reviewer selects right-hand variants; record and review whole clips, tune on separate performances, then run held-out/live Quest Link validation and save measured settings/results. |

J/Z is complete when a user can record/review/save whole examples, recognize complete live performances with one event each, see useful failure feedback, and retain all prior static behavior, with actual software/device evidence reported. Future two-hand support is prepared when its optional slots, common coordinates, role mapping, simultaneous static-sample contract, and shared motion-alignment extension are documented and compatible; that preparation must not be described as already recognizing two-hand signs.
