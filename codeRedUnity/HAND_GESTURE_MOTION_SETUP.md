# J/Z motion: implementation, setup and verification

Implemented October 11, 2026 against `HAND_GESTURE_MOTION_AND_TWO_HAND_HANDOFF.md`. Phases A–E supply an opt-in, right-dominant J/Z recording and recognition prototype. Phase F (Quest Link recordings, ASL review, calibration and held-out evaluation) has **not** been performed. Left-hand production and all two-hand evaluators remain future work. No real training examples or recognition-accuracy claims are supplied.

## Structure and compatibility

All additions use the existing `HandPoseRecognition` namespace and Runtime, Editor and EditMode assemblies. No package, assembly, legacy enum, static schema, joint layout or canonical normalization version changed. `HandPoseDemo.unity`, `HandPoseRecognitionDemo.unity`, saved static templates, existing test methods, classifier bodies and tuned settings are preserved.

The only edit to an existing runtime script is additive: `QuestHandAdapter` implements optional `IHandFrameStream` and publishes the same immutable `HandFrame` after every Dynamic acquisition or explicit loss. BeforeRender remains excluded. Existing `IHandFrameSource` readers still work. Installed XR Hands 1.5.1 `XRHandSubsystem.cs` was checked for Dynamic/BeforeRender callback behavior; no new XR API was invented.

| File under `Assets/Scripts/HandPose/` | Responsibility |
| --- | --- |
| `IHandFrameStream.cs` | Optional acquisition event, preserving the existing source interface and snapshot ownership. |
| `GestureDefinition.cs` | Category, J/Z kind, explicit physical dominance/signing roles, phase gates, spatial extension and validated prototype budgets. |
| `GestureObservation.cs` | Owned optional Left/Right raw evidence, source identity, time/acquisition/epoch, numeric calibration and frozen reference. Explicit slot-presence flags survive Unity serialization. |
| `GestureClip.cs` | One owned ordered performance, relative timestamps and structural validation; separate simultaneous `GestureStaticSample` extension with no trajectory requirement. |
| `GestureData.cs` | New authoring asset holding a frozen definition and complete alternative clips; optional static arrangements and existing static shape references for future evaluators. |
| `GestureCatalog.cs`, `GestureCatalogSnapshot.cs` | Bounded explicit catalog, atomic validation, ordinal IDs, immutable runtime definitions and preprocessed examples. Unsupported categories remain marked unsupported. |
| `GestureFeatureExtractor.cs` | Fixed-reference tip/wrist paths, wrist orientation, evidence validation, jump rejection, timestamp-aware smoothing and bounded resampling. |
| `MotionGestureRecognizer.cs`, `GestureRecognitionResult.cs` | Candidate-specific gates, independent complete-phase checks, bounded endpoint DTW, deterministic score/threshold diagnostics and source provenance. |
| `GestureRecordingSession.cs` | Injected-clock Begin/Tick/Stop/Cancel, bounded distinct acquisitions, invalid review reports, owned export and explicit revalidated trimming. |
| `GestureRecognitionSession.cs` | Armed onset history, tracking, terminal pause, one completion event, release/cooldown and reset/loss handling. |
| `GestureReferenceProvider.cs` | Camera Offset/XR camera calibration, rig-change epoch detection and explicit recenter reset hook. |
| `HandGestureInput.cs` | Shared thin source binding for recorder/controller, stream subscriptions, latest-frame fallback, source availability and epoch invalidation. |
| `HandGestureRecorder.cs`, `HandGestureController.cs` | Unity clock/source bridges, explicit recording and catalog rebuild, opt-in JZOnly lesson mode. |
| `HandGestureDebugDisplay.cs` | Separate TMP label and complete 3D trail; state, duration, phases, score availability, best example and reason. |
| `Editor/GestureRecorderWindow.cs` | Whole-clip review, two trail projections, provenance, explicit trim/keep/save and actual editor lifecycle retention hooks. |
| `Editor/GestureRecordingPersistence.cs` | Create/append/replace, prospective validation, Undo, save/reimport/reload and SessionState review payloads. |
| `Editor/HandGestureMotionDemoSetup.cs` | Unity-created opt-in scene copy, calibration/source wiring, separate label/trail and empty recorded catalog. |

New Assets files have Unity-generated `.meta` files. The delivered `Assets/Scenes/HandGestureMotionDemo.unity` defaults to **Motion Disabled**, retains the original legacy display/settings and uses a separate motion label. `Assets/HandGestures/Recorded/MotionCatalog.asset` is deliberately empty. It contains no synthetic geometry presented as ASL training data.

## Data and coordinate contracts

Gesture schema, feature, preprocessing and calibration versions are **1**. Old static sample/template/database schema 1 and layout/normalization 1 are unchanged. A `HandFrame` still represents one physical Left or Right hand; there is no `Both` value. Definition roles map Dominant/NonDominant to explicitly configured physical sides. The current producer/evaluator accepts only right-dominant, one-hand J/Z; left-dominant, static, both-role, role-exchange or required cross-hand-constraint entries cannot silently become one-hand matches.

Each clip owns its array and frozen definition, and retains immutable raw observations containing all 26 joint slots, availability, source-local hand sequence/time and original rotations. The observation acquisition ID is producer-wide; optional left and right frame sequence IDs are never compared with one another. Catalog snapshots own their definitions/examples and static shape copies; rebuilding after an asset edit cannot mutate an older published snapshot. Catalog build fails atomically on duplicates, invalid children or exceeded budgets.

At Begin/Armed, freeze the calibrated basis and initial index-proximal to little-proximal palm width. Up is world vertical converted into tracking space; forward is headset forward projected onto its horizontal plane. The input rig must have finite positive uniform scale without reflection or shear. No camera lookup occurs in pure feature extraction.

The main tip path is `inverse(frozenBasis) * (rawTip - initialTip) / initialPalmWidth`. The wrist path uses its own initial wrist in the same fixed basis/scale. Wrist orientation is `inverse(frozenBasis) * rawWristRotation`. Orientation distance uses `abs(dot(q1,q2))` with double-precision norm correction, so equivalent q/−q representations agree. No Euler subtraction, moving-wrist subtraction, per-frame basis following or implicit hand mirroring occurs. Raw 3D evidence is retained; a two-dimensional review projection does not replace it.

Future static samples hold simultaneous canonical role shapes plus shared relative palm position and wrist rotation. Relative position is defined in a common reference/scale; relative rotation is `inverse(nonDominantRotation) * dominantRotation`. Future spatial constraints reserve cross-hand joint IDs and distance bounds. Their evaluator/coherent producer is explicitly absent. J/Z assets do not require a left source and have an explicit absent-left slot after serialization/reload.

Rig transform/reference changes advance a coordinate epoch and clear active runtime evidence or invalidate recording. Head motion alone does not change the epoch or an active frozen basis. **Call `GestureReferenceProvider.ResetCoordinates()` after recentering or any tracking-origin discontinuity that does not change the Camera Offset transform.** The current integration does not claim automatic detection of every provider recenter. Old cached acquisitions cannot be read into the new epoch before a new source sequence arrives.

## Complete-sequence rules

Z uses an extended index with curled middle/ring/little fingers. J uses an extended little finger with the other non-thumb fingers curled. Thumb geometry must be available and satisfy the configured raw-ratio policy. These candidate-specific predicates reuse existing pure finger/ratio math; they do not depend on the globally winning static template or change legacy classification. Finish-phase thresholds allow configured variation. Missing required positions, missing required wrist orientation, bad metadata, nonmonotonic acquisition/time, tracking gaps and implausible positional jumps fail explicitly.

Preprocessing 1 applies exponential smoothing with `alpha = 1 - exp(-dt / smoothingSeconds)` and shortest-path quaternion interpolation, then resamples the valid entire clip to a fixed count in relative time. Original duration and timestamps remain available. It never interpolates over missing tracking. Stored examples are preprocessed once when building a snapshot; live replay is processed under the candidate's settings.

Z requires an ordered top stroke, downward diagonal reversal and bottom stroke. J requires descent before its lateral/upward hook and retains wrist rotation in scoring. Duration, excursion, path length and all shape phases must also pass. A held I/pointing shape, straight swipe, reversed sequence or unfinished prefix cannot confirm merely because a partial distance is small.

DTW begins at both first samples and ends at both last samples, with a bounded diagonal band and consecutive horizontal/vertical run cap. Local cost is the weighted mean of tip distance (palm widths), wrist distance (palm widths) and shortest wrist angle divided by its configured degree scale. Finite double accumulation is divided by the chosen alignment path length. The globally lowest eligible complete score wins; exact ties use ordinal gesture ID then stable example index. That winner must pass **its own inclusive threshold**. A worse candidate with a wider threshold does not replace it. Score is distance, never a probability or percent correct. Failed/unavailable observations have no score or matched ID; AboveThreshold retains only a diagnostic candidate/error.

## Timing, recording and review

Manual recording freezes definition/settings, identity, physical role mapping, reference and limits at Begin. It admits every distinct raw Dynamic acquisition, without inheriting the static 0.1-second evaluation interval. Duplicates cannot add attempts or evidence. Incoming pairs are validated incrementally; the recorder does not recopy/revalidate its entire growing buffer at every acquisition. A source/epoch change, tracking gap, invalid clock, missing evidence or disabled/destroyed required source makes the review invalid. Reaching timeout/frame/attempt caps is **not** successful completion. Stop may yield an incomplete/unsavable review; Cancel discards it. Explicit trimming retains the frozen axes/scale, rebases path/time to the selected start and revalidates the entire remaining performance.

Runtime transitions are Idle → Armed → Tracking → Completed → AwaitRearm. Invalid partial performances go to Rejected/AwaitRearm. Armed keeps a short bounded onset history so movement detection retains the first stroke. The two-second attempt deadline includes time Armed. A terminal pause triggers full replay only after ordered phase coverage. Pause/dwell require advancing acquisitions; merely rereading a frame cannot complete motion. Successful completion emits `GestureConfirmed` once with interval, physical side/roles, epoch/source and last acquisition ID. Holding the end pose or cooldown expiry cannot replay it. Rearm requires distinct valid observations outside **all** enabled starting gates spanning the release period, plus cooldown. Missing tracking does not count as a release. Context changes and disable/re-enable clear all evidence and reset cooldown.

The opt-in controller offers Disabled and **JZOnly**. This is the handoff's permitted J/Z-only lesson arbitration: this path never emits a static letter during a motion prefix or a held starting shape. Existing legacy/static components remain independent diagnostics; a future mixed lesson should consume an explicit arbitration layer rather than subscribe indiscriminately to both event sources.

Open **Tools → Hand Gesture → Recorder Review before recording** and keep it open. Stop/close/reload/Play Mode-exit hooks retain an owned whole-clip JSON review in Editor SessionState; reopening restores it. Review data survives domain/Play Mode transitions, **not closing Unity**. Explicitly save before editor shutdown. No real asset is automatically written on Stop, disable, window close or exit. Recording started exclusively by another script without the review window does not promise editor retention.

Saving requires an explicitly kept, valid whole performance. Create chooses a unique path under an existing non-synthetic `Assets/HandGestures/` folder. Append/Replace require matching frozen identity, versions, roles, feature settings, budgets and provenance; use the window's **Copy saved definition** control before the next capture. The complete proposed asset and optional complete prospective catalog are validated before edits. Duplicate IDs, incompatible definitions, invalid clips and exceeded budgets fail without partial writes. Catalogs must be persistent assets. Undo groups cover edits; saved assets/catalogs are reimported, reloaded and validated. Explicit controller rebuild publishes the replacement snapshot. The separately labeled retain-on-reload-failure button is the only way to keep a prior snapshot after a failed reload.

## Provisional saved defaults

These are engineering seeds, not calibrated ASL thresholds. Recorded clips freeze all of them. Configure a separate reviewed definition/variant deliberately when changing them.

| Setting | Default |
| --- | --- |
| Duration | minimum 0.15 s; maximum 2 s |
| Frames / attempts / alternative clips | 256 / 512 / 8 |
| Catalog entries / total clips | 64 / 256 |
| Freshness / maximum acquisition gap | 0.25 s / 0.12 s |
| Smoothing / resampled points | 0.025 s / 64 |
| DTW band / maximum consecutive warp run | 12 / 2 |
| Inclusive maximum error | 0.25 |
| Minimum excursion / path length | 0.45 / 0.8 palm widths |
| Maximum tip or wrist speed | 40 palm widths/s |
| Tip / wrist / orientation weights | 1 / 0.15 / 0.15 |
| Orientation scale | 90 degrees; positive orientation weight required for J |
| Stroke threshold / onset threshold | 0.15 / 0.12 palm widths |
| End pause / pause movement threshold | 0.18 s / 0.04 palm widths |
| Onset history | 0.15 s, at most 12 frames |
| Release / cooldown | 0.2 s / 0.5 s |
| Signing direction | explicit +1 initially; must be reviewed for the selected recorded variant |
| Starting curl gates / finish gates | 70/160 degrees / 95/140 degrees |
| Finish phase begins | 0.65 of clip duration during complete replay |
| Thumb extended / curled ratios | 0.75 / 0.45; NotExtended policy initially |

Hard validation caps are 4,096 raw frames, 16,384 attempts, 256 resampled points, warp run 16, 32 examples per definition, 256 catalog entries and 1,024 catalog clips. Malformed settings fail; no clipping or silent truncation occurs. Static future entries need no motion settings or DTW trajectory.

## Exact Unity and Quest Link workflow

1. Run the preserved `HandPoseDemo.unity` on Quest Link and verify the existing five digits/eight legacy poses, tracking loss and saved tuning. This device check remains unperformed here.
2. Open **`Assets/Scenes/HandGestureMotionDemo.unity`**. On **Hand Pose Recognition**, the recorder/controller Source both reference the existing right-hand QuestHandAdapter. Their Reference Provider references GestureReferenceProvider. Its Tracking Space is **Camera Offset**, and Head is **Main Camera**. The controller Catalog is the empty **MotionCatalog**. Mode defaults to **Disabled**. The separate debug component references the controller, **Motion Debug Label**, and **Complete motion trail** LineRenderer. The original label retains its single legacy writer.
3. To make another independent copy, use **Tools → Hand Gesture → Create Opt-in Motion Demo Copy**. It uses Unity AssetDatabase/scene APIs, chooses unique paths and leaves existing scenes and Build Settings unchanged.
4. Open **Tools → Hand Gesture → Recorder Review** and choose HandGestureRecorder. Enter a stable gesture ID/display name, Motion kind J or Z, Right dominance and variant/reviewer/provenance notes. Select the intended signing direction, thumb configuration and settings using the intended real demonstrations. For an existing gesture, copy its exact saved definition before recording another alternative.
5. Enter Play Mode with motion Disabled. Face a comfortable forward direction, hold the starting shape, press **Begin complete labeled performance**, perform the whole sign, then **Stop and review entire clip** before the capture limit. Keep the window open. Check frame/attempt counts, start/end sequences, duration, phase coverage, failure reasons and both trail projections. Invalid captures remain visible and cannot be saved. Explicit trim is optional and reruns complete validation.
6. Exit Play Mode with the window open. Mark **Keep this entire reviewed performance**. Select Create (or an explicit compatible Append/Replace), assign the saved MotionCatalog in the optional catalog field and save under **Assets/HandGestures/Recorded/**. A successful report means save/reimport/reload validation succeeded. Keep varied whole performances as alternatives; never split neighboring frames into independent training/test examples.
7. Select the controller and press **Rebuild gesture catalog**. Inspect its report. Enter Play Mode and deliberately choose **JZOnly**. Form the initial shape, perform the complete motion and pause briefly at the end. Inspect state/phase feedback and the separate error/best-example diagnostics. Subscribe to `GestureConfirmed` for the new lesson path. Release the starting shapes before repeating.
8. Verify on-device loss/recovery, partial trails, excessive duration, straight swipes, held I/pointing shapes, wrong/reversed strokes and end-pose holds. Rotate/move the head after arming: axes must remain fixed. Recenter or teleport only after calling **ResetCoordinates** (also exposed by the reference provider's context menu); do not join trails across discontinuities.
9. Return Mode to Disabled to stop the motion lesson path. The original legacy display/settings remain available. Save only deliberate scene/definition tuning; do not infer ASL accuracy from a test fixture or a low error on its own recorded example.

Reference material was checked while implementing the contract: [Lifeprint fingerspelling guidance](https://www.lifeprint.com/asl101/pages-layout/fingerspelling.htm), [J reference](https://www.lifeprint.com/asl101/pages-signs/j/j.htm), and [Z reference](https://www.lifeprint.com/asl101/pages-signs/z/z.htm). No real performance was selected or approved by an ASL-fluent reviewer during this work. Numeric direction/phase tolerances remain provisional.

## Verification and remaining work

Unity **6000.0.34f1**, XR Hands **1.5.1**, OpenXR **1.14.0**, Test Framework **1.4.5**. Tests use the isolated workspace `.verification/HandPoseBaseline`, synchronized from current Assets/Packages/ProjectSettings. The user's open editor/scenes were not used as the test runner. Unity creates the new asset GUIDs.

Before implementation, the current baseline was **392 total: 391 passed, 1 failed**. The existing `RecognitionDemoSetupTests.OptInScenePreservesTuningSideRigAndLegacyDefaultAndAddsWiring` expects the saved recognition demo to use LegacyRules, while the current saved scene selects Template. That scene/test mismatch predates these changes. Neither scene nor existing test was edited to hide it.

Final verification on October 11, 2026: **461 total, 460 passed, 1 failed**. All **69 new gesture tests passed**; the sole failure is the same baseline mismatch. No C# compilation errors or warnings were reported in the final log. Evidence: [baseline XML](Verification/HandGestureMotion/baseline.xml), [baseline log](Verification/HandGestureMotion/baseline.log), [final XML](Verification/HandGestureMotion/final.xml), [final log](Verification/HandGestureMotion/final.log), and [Unity demo-generation log](Verification/HandGestureMotion/demo-setup.log). The full suite correctly exits with failure because the baseline mismatch is unresolved; this is not an entirely green test suite.

The automated command is:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.0.34f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'C:\Users\creep\Desktop\Projects\codeRed 2026\.verification\HandPoseBaseline' -runTests -testPlatform EditMode -testResults '<evidence>/final.xml' -logFile '<evidence>/final.log'
```

Software checks cover ownership/versioning, physical roles and absent-hand persistence; transformed/scaled and different-speed J/Z; hand-computed DTW cost/endpoints/stretching/ties/thresholds; complete phases versus held/partial/reversed/wrong-shape trails; required-joint/rotation loss, sequence/time/source/epoch failure; bounded recording and explicit trim; atomic catalog/save/append/replace/Undo; actual window retention through Play Mode/domain transitions; raw Dynamic stream behavior and component capture/confirmation/disable; one event per performance and release/cooldown; fixed axes under head changes; and the separate disabled-by-default demo.

Headless Play Mode native OpenXR `XR_ERROR_FORM_FACTOR_UNAVAILABLE`/display initialization diagnostics and licensing startup diagnostics are separate from C# compilation and managed test outcomes. They do not constitute successful headset initialization. The interactive editor Console was not inspected or cleared.

The profiling test uses a visible 1,024-byte allocation positive control, current-thread Unity GC.Alloc event recording with wraparound checks, 128 warm-up and 128 measured calls. Workload: one source, one right-side Z definition, one 43-frame example, 64 resampled points, band 12/run 2. Raw HandFrames are preconstructed to separate existing acquisition cost. Recorder capacity is warmed. Text formatting is measured separately from geometric work; headset graphics/LineRenderer cost is not measured by headless tests. Counts are allocation **events**, not allocated bytes; timings are Editor-host throughput, not Quest player latency.

Measured in the final run (128 calls per row):

| Work | Total allocation events | Events/call | Microseconds/call |
| --- | ---: | ---: | ---: |
| Owned gesture observation construction | 385 | 3.008 | 1.262 |
| Complete-clip extraction/preprocessing | 2,560 | 20.000 | 114.755 |
| Complete replay, validation and DTW | 7,296 | 57.000 | 1,215.131 |
| Recorder incoming-frame admission | 1 | 0.008 | 2.616 |
| Developer debug text formatting | 2,048 | 16.000 | 4.970 |

These are single-run measurements including the recorded outlier allocation events, not a zero-allocation guarantee. Recorder measurements include the repeated initial acquisition and 127 new acquisitions, use preconstructed observations and warmed capacity, and exclude review/export. Runtime boundary scanning and the full rendered display are not separately benchmarked here.

The default rolling DTW workspace is **7,680 bytes** for its reused cost/path-length arrays; at the hard 256-point/run-16 limit it is **202,752 bytes**. Owned raw frames/observations, live preprocessing arrays, validation reports and review/UI formatting still allocate. No retained frame/sample array is pooled or reused. Further optimization should follow target-player profiling rather than weaken ownership.

Remaining phase F evidence: actual Quest Link baseline and capture; reviewed real J/Z variants; final signing-direction/thumb/shape/motion thresholds; training/tuning/held-out splits by whole performance; accepted/rejected counts and false activations on negatives/transitions; signer coverage, confusion and confirmation latency; target-headset capture/extraction/matching/visualization CPU, allocation bytes and memory. No such measurements or accuracy results are claimed.

Next concrete task: an ASL-fluent reviewer selects the intended right-hand J/Z variants; record a few complete clips through the delivered review workflow, tune on separate complete performances, and run held-out/live Quest Link evaluation. Future G/H/I then implement coherent paired production, a true static two-hand evaluator without DTW, and shared-alignment moving two-hand evaluators. COFFEE/TEA remain unsupported acceptance targets, not delivered recognizers.
