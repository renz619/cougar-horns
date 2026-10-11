# Hand pose recognition: implementation, setup and verification

Implemented and verified 2026-10-11 against `HAND_POSE_RECOGNITION_HANDOFF.md`. The assigned software scope is complete. Quest Link checks and recognition calibration remain unperformed.

## Files and APIs

All runtime additions use the existing `HandPose.Runtime` assembly and `HandPoseRecognition` namespace. No packages or assembly boundaries were added.

| File under `Assets/Scripts/HandPose/` | Role and public surface |
| --- | --- |
| `RecognitionResult.cs` | Immutable observation and explicit `RecognitionStatus`; `PoseId`, diagnostic `BestCandidateId`, double `Error`, `HasScore`, `IsMatch`, side/time/sequence and sample index. Also contains the shared internal frame-admission check. |
| `ShapeRecognizer.cs` | Pure static `Recognize(canonical, catalog, nowSeconds, maxFrameAgeSeconds)`. Does no normalization, catalog rebuild, hardware access, asset mutation or timing state. |
| `PoseRecordingSession.cs` | Plain bounded capture; `PoseRecordingSettings`, states/completion reasons, `Begin`, injected-clock `Tick`, `Stop`, `Cancel`, `Clear`, counts/rejection reason and `ExportReview`. `PoseRecordingReview` owns serializable copied definition/samples. |
| `HandPoseRecorder.cs` | Thin MonoBehaviour source/Unity-clock bridge. `Source`, `Settings`, `Session`, explicit `Begin`, `Stop`, `Cancel`. No automatic recording, matching or asset writes. |
| `RecognitionSession.cs` | Plain injected-clock timing policy, backend/timing types, latest observation, pending/confirmed IDs, status/reason and `PoseConfirmed` event. |
| `RecognitionController.cs` | MonoBehaviour source/clock bridge; backend/source/database selection, enable-time build, explicit `RebuildCatalog(retainPreviousOnFailure = false)`, report, confirmed display name, and forwarded provenance-bearing events. |
| `HandPoseDisplay.cs` (additive changes) | Optional controller, explicit backend selection, template-result/ID accessors and status/error formatting. Keeps legacy classification/settings and raw finger/spread/orientation diagnostics. |
| `PoseDatabase.cs` (one additive API) | `CopyTemplates()` gives editor persistence an owned reference-array copy for validating complete prospective catalog updates. |
| `Editor/PoseRecordingPersistence.cs` | Explicit Create/Replace/Append save; validates selections, full proposal and prospective catalog before mutation, supports Undo, saves/reimports/reloads/validates. `PoseReviewRetention` retains JSON review payloads through editor SessionState. |
| `Editor/PoseRecorderWindow.cs` | Recorder selection/settings, explicit capture controls, counts/reasons/provenance, per-sample keep/remove, save controls; controller Inspector rebuild buttons; `HandPoseRecognitionDemoSetup.Create` copies and wires an opt-in scene using Unity editor APIs. |
| `Assets/Scenes/HandPoseRecognitionDemo.unity` | Unity-created, saved opt-in copy with adapter/controller/recorder/display wiring; default backend LegacyRules. Supplied catalog remains clearly synthetic. |

Unity generated the `.meta` files for new Assets files. The original `HandPoseDemo.unity`, existing GUIDs, enum values, classifier/settings, protected classifier bodies and existing test methods remain unchanged by this assignment. The acquisition and test-assembly changes already present at the start were preserved. Original scene tuning remains curl **70/160**, thumb **0.75/0.45**, spread **0.10/0.25**, world-up angle **35 degrees**, Right hand and Camera Offset. The opt-in scene test compares its tuning/references with the preserved scene.

## Matcher contract

Input must be usable, tracked, known-side canonical space, layout 1 and normalization 1. Frame constructors reject unsupported versions and malformed timestamps; admission also checks space, validity, finite nonnegative caller time, finite positive age bound, future time and freshness. Age exactly equal to the bound is accepted. Catalogs are built/validated separately and provide immutable cached side buckets.

For each sample, sum `weight * EuclideanDistance(samplePosition, livePosition)` over all positive-weight joints and divide by their complete weight sum. Differences, distances, accumulation and division use double intermediates with finite checks. Missing any required position disqualifies that comparison; zero-weight joints and optional rotations do not participate. The winning sample has the global lowest error; exact ties use ordinal PoseId, then stable sample index. Its own threshold is inclusive. A wider threshold on a worse candidate does not change the winner.

Default and failed/unavailable observations have no score or matched ID. AboveThreshold has a finite score and diagnostic best-candidate ID, but no matched PoseId. Matched has a known ID and score. Controller normalization failures have their actual normalization reason and source provenance, with no invented score. Shape IDs do not map to `HandPose` enums and scores are distances, not confidence/probability.

Matching scans immutable samples directly in O(samples * 26), with no candidate arrays, LINQ materialization, copies, rebuilds or asset access. Match-loop enum formatting allocations discovered by the measurement probe were removed before final verification.

## Recording, review and persistence

Begin validates and copies the definition, all 26 weights, threshold, side, template budget, limits and source identity. Inspector edits cannot change an active session. Defaults are 32 accepted samples, 256 distinct-frame attempts, 10 seconds, minimum accepted-frame interval 0.1 seconds and maximum age 0.25 seconds. Malformed settings fail; limits are never clamped. Accepted count must fit the template budget.

Every distinct acquisition counts once, including rejections; duplicates never count again. Raw side/space/time/freshness/tracking checks precede one shared normalization attempt. Missing anchors and required weighted joints reject with reasons. Interval-skipped acquisitions count as rejected. The sample interval uses acquisition timestamps and accepts equality. A valid stopped session may have zero samples, but cannot be saved. Time reaches its bound at `now - begin >= duration`, including no-update sessions. Reaching an accepted/attempt bound stops immediately, with accepted limit taking precedence when both are reached together. Source replacement and invalid/nonmonotonic clock terminate with separate completion reasons.

Stop produces ReadyForReview; Cancel discards explicitly; Clear resets to Idle. Export requires recording to have stopped, and copies retained canonical samples/provenance in capture order. Review accessors return copies. There is no ring buffer or automatic template write on stop, disable, reload or Play Mode exit.

**Use the Recorder Review window before beginning and keep it open during capture.** Its stop/close/reload/Play Mode-exit hooks stop a selected active recording and retain the owned review JSON in SessionState. Reopening the window restores it. SessionState survives domain reload and leaving Play Mode, **but does not survive closing Unity**. Explicitly save before closing Unity. A component started by an external script without this window does not promise editor retention. The retention test records and restores a payload through actual EnterPlayMode/ExitPlayMode domain transitions; serialization and saving restored samples are also tested. Selected rows are serialized editor-window state; none are selected implicitly for saving.

After Play Mode exits, keep/remove representative samples and explicitly save a nonempty selection. Create uses a unique asset path within an existing `Assets/HandPoses/` folder. Replace/Append require an existing compatible non-synthetic template and preserve its ID/display name. ID, side, schema/layout/normalization, weights, threshold and sample budget must match the frozen definition. Append checks the combined budget before writes. The full proposed definition is validated using `HandPoseData.SetDefinition` and the foundation validation path. An optional catalog update validates its entire prospective reference set, including duplicate IDs, before changing either asset. Synthetic fixture templates/catalogs are protected from reviewed updates. Asset edits support Undo, save/reimport/reload validation, and require an explicit controller snapshot rebuild; old snapshots remain immutable.

## Controller policy

Defaults are maximum age 0.25 seconds, evaluation interval 0.1 seconds, dwell 0.2 seconds and event cooldown 0.5 seconds. Age must be finite positive; interval/dwell/cooldown finite nonnegative. Invalid settings disable the template observation path. Availability/tracking/freshness are checked on every update, including repeated sequences and rate-limited updates. Any failure clears pending/confirmed state immediately.

Each new admitted source sequence is normalized at most once and its canonical data is cached. Only the latest skipped acquisition is retained for the next permitted evaluation; there is no backlog. Repeated already-evaluated sequences do not recompute matching or advance dwell. Older sequences are rejected. Interval equality permits evaluation. Dwell starts at the first eligible matched acquisition's timestamp; confirmation requires distinct evaluated matches whose acquisition timestamps span the inclusive dwell boundary. Delaying evaluation cannot manufacture dwell evidence. Zero dwell permits confirmation on the first eligible acquisition. Different IDs, nonmatches and failures reset the hold; nonmatches release immediately when evaluated.

`LatestObservation` remains a truthful matcher result even while controller status is Holding. Confirmed ID is separate. One event is emitted per newly confirmed hold. Continuing to hold never emits again after cooldown. Releasing/reconfirming creates a new hold; cooldown may suppress its event while the confirmed ID remains true. Once cooldown inclusively expires, that hold can emit once if the supporting observation remains fresh. Events carry matched ID, score, side, timestamp, sequence and sample index. Source/backend/snapshot replacement and disable/reset clear caches and holds. **Reset/re-enable/context replacement resets cooldown**; this policy is deterministic and tested. Disable unsubscribes event forwarding.

Enable builds the catalog once; Inspector database-reference replacement invalidates the previous snapshot and requires explicit rebuild. Ordinary rebuild failure leaves unavailable. The separate explicit retain-on-failure action may keep a prior snapshot and prominently exposes the failed report. No silent legacy fallback or implicit lesson behavior occurs.

Display defaults to the original legacy path with no controller. Template mode shows Unknown/Holding until confirmation, then template DisplayName/PoseId, controller status and observation error or n/a. Legacy `currentPose` is Unknown throughout Template mode. It retains its last Template selection when the controller is disabled/destroyed/disconnected. Returning to LegacyRules requires an explicit mode action. One display component writes the existing TMP label; the controller never writes it.

## Exact Unity setup and Quest Link workflow

1. Connect Quest Link, enable hand tracking, put controllers down, and run the preserved `Assets/Scenes/HandPoseDemo.unity`. Check all five digits and existing Open/Fist/Pointing/V/U/Y/L/ThumbsUp behavior with its saved tuning. This is a device check to perform, not a reported pass.
2. Exit Play Mode and open `Assets/Scenes/HandPoseRecognitionDemo.unity`. It contains the copied XR rig, label and original `Hand Pose Recognition` GameObject. The adapter remains Right with **Camera Offset** assigned. RecognitionController Source and HandPoseRecorder Source both reference that adapter. HandPoseDisplay Controller references RecognitionController; its adapter/label/settings retain their original references/values. Controller backend is initially **LegacyRules**, and Database is the supplied synthetic database.
3. If another opt-in copy is wanted, use **Tools > Hand Pose > Create Opt-in Recognition Demo Copy**. It copies the original via AssetDatabase, assigns the above references and saves a unique scene path. It never regenerates or saves over the original and does not change Build Settings.
4. In the Project window, create an `Assets/HandPoses/Recorded` folder and a new database via **Create > Hand Pose > Database**. Give it a descriptive name such as `RecordedShapesDatabase`. Keep the synthetic database as a fixture; reviewed saves intentionally reject updating it.
5. Open **Tools > Hand Pose > Recorder Review**, select the copied scene's HandPoseRecorder, and inspect Source/Settings. Enter a stable descriptive ID (for example `recorded.user.shape01`), display name, Right side, 26 weights, threshold and sample budget/limits. Labels describe actual recorded shapes; no validated ASL accuracy is implied. Keep the window open, enter Play Mode with backend LegacyRules, hold the intended shape and press **Begin labeled capture**. Inspect accepted/rejected/attempt counts and actual rejection reasons. Stop explicitly, or let the documented limit stop the capture.
6. Review provenance rows and explicitly select representative samples. Exit Play Mode with the window open; the retained candidate data restores through domain reload. Save controls become available outside Play Mode. Choose **Create**, assign the new recorded database in the window's optional catalog field, and press **Save selected samples explicitly**. Choose a filename in `Assets/HandPoses/Recorded/`. A successful report confirms save/reimport/reload validation. To replace/append later, use the explicit dropdown action and a compatible existing destination; combined-budget/duplicate/compatibility failures leave destinations untouched.
7. On the copied scene's controller, assign `RecordedShapesDatabase`, press **Rebuild catalog (failure makes unavailable)**, and inspect the report. Select the catalog itself and its existing **Build and validate runtime snapshot** button if authoring diagnostics are needed. Save only this opt-in scene, keeping its default backend LegacyRules if it will be shared as an opt-in setup.
8. Reenter Play Mode, deliberately select controller **Backend = Template**, and hold the recorded shape. Inspect geometric error, observation status, Holding and Confirmed name/ID. Repeat acquisitions must span the dwell. Hide the hand and confirm immediate Unknown/loss; show it again and inspect reconfirmation/event cooldown. These are real-device checks still to perform; synthetic integration proves the component flow only.
9. Deliberately select **LegacyRules** and verify restoration of the eight old poses without changing saved thresholds. Repeat with nonidentity Camera Offset/XR Origin rotation: old world-up thumb conversion must still occur exactly once, while raw acquisition remains tracking space.

## Verification and archived evidence

Unity **6000.0.34f1**, XR Hands **1.5.1**, OpenXR **1.14.0**, Test Framework **1.4.5**. Tests ran in the existing isolated workspace `.verification/HandPoseBaseline` with current Assets and matching Packages/ProjectSettings, preserving the user's open editor. Package/project configuration equality was verified by file hashes. The current 294-test baseline ran before copying recognition additions into that project.

| Run | Actual results | Archived XML |
| --- | --- | --- |
| Current foundation baseline | 294 passed, 0 failed, 0 skipped | `Verification/HandPoseRecognition/baseline.xml` |
| Matcher and recorder phase | 343 passed, 0 failed, 0 skipped | `Verification/HandPoseRecognition/matcher-recorder.xml` |
| Final full suite, including Play Mode component/retention/allocation tests and scene verification | **392 passed, 0 failed, 0 skipped** | `Verification/HandPoseRecognition/final.xml` |

Additive tests are `ShapeRecognizerTests.cs`, `PoseRecordingSessionTests.cs`, `PoseRecordingPersistenceTests.cs`, `RecognitionSessionTests.cs`, `TemplateDisplayIntegrationTests.cs`, `RecognitionComponentPlayModeTests.cs`, `RecognitionAllocationTests.cs` and `RecognitionDemoSetupTests.cs`. Coverage includes hand-computed weighted distance; sample minima/ties; threshold/freshness boundaries; unknown/missing/unsupported input; extreme arithmetic; ownership; bounds/deduplication/loss/recovery; create/save/unload/reload; append budget/identity; duplicate-catalog atomic rejection; snapshot immutability and Undo; serialized review retention through actual Play Mode/domain transitions; interval/distinct-acquisition dwell; delayed evaluation; immediate clearing; re-hold/cooldown/events; enable/context changes; failed catalog reload; missing/disabled/destroyed controller; legacy restoration; and opt-in scene references/tuning.

The actual component Play Mode test drives a synthetic `IHandFrameSource` through HandPoseRecorder, RecognitionController and HandPoseDisplay, confirming capture, match, loss, recovery, disable and explicit LegacyRules restoration. Existing synthetic XR-provider/acquisition tests also pass. These are not headset tests.

Intermediate verification found four new display-fixture failures caused by assuming automatic MonoBehaviour callbacks in EditMode; fixtures were corrected to invoke them explicitly. The subsequent allocation test exposed two enum-formatting allocations per match; fixed before the final run. No existing test was edited to obtain a pass. Intermediate XML/logs are retained under workspace `.verification/Recognition/`.

C# compilation: final Unity run completed without C# compiler errors. Test Console evidence: no unexpected managed LogError/exception test failures. The user's interactive Console was not inspected or cleared. Native initialization: headless Play Mode logs contain OpenXR **XR_ERROR_FORM_FACTOR_UNAVAILABLE** / display initialization failures. Startup also reports licensing-client signature/access-token diagnostics before successfully using its entitlement. These are recorded separately from passing synthetic tests and are not successful device initialization evidence. Full baseline/final logs and the successful Unity scene-generation log are archived alongside XML; demo generation exited with code 0.

## Measured performance

Final Play Mode profiling uses a visible 1024-byte known-allocation positive control and Unity GC.Alloc recording on the current thread, with no recorder wraparound. It warms 512 iterations, then measures 512 calls for each operation separately. Fixed input is one source, one same-side candidate, one sample, 26 positive weights. Raw/canonical acquisitions are preconstructed to exclude source capture cost. Recorder capacity is warmed using an explicitly configured 512-sample test budget; the shipped capture defaults remain 32 samples. Stopwatch elapsed times measure synthetic Editor-host throughput, not Quest player CPU or frame timing.

| Measured operation | Total allocation events / 512 calls | Events / call | Elapsed microseconds / call |
| --- | ---: | ---: | ---: |
| Pure matcher | 0 | 0 | 2.942 |
| Normalization only | 1536 | 3 | 8.926 |
| Controller helper including normalization/matching | 1536 | 3 | 10.658 |
| Recorder helper including normalization/owned sample conversion | 57344 | 112 | 33.358 |
| Controller repeated fresh acquisition | 0 | 0 | 0.183 |

These are **allocation-event counts, not allocated bytes**. Recorder conversion invokes the foundation's per-sample validation/report/path machinery as well as owned sample copying; its measured allocations are reported without an allocation-free claim. There is no pooling or reuse of arrays owned by retained frames/samples/catalogs. The original acquisition probe still runs in the full suite and reports its separate synthetic Dynamic capture/normalization counts. Target-player byte sizes, CPU and suitability must be measured on Quest before optimization or calibration decisions.

## Remaining device verification

No Quest Link result or calibrated recognition accuracy is claimed. Remaining checks: all five digits/eight legacy poses; tracking loss/recovery and source shutdown; real frame side/availability; nonidentity Camera Offset/XR Origin conversion once; bounded real capture and reviewed save/reload; real Template dwell/confirmation/loss/cooldown; explicit LegacyRules restoration; target-player CPU/allocation profiling. Synthetic fixtures remain clearly labeled and are not real open-hand/ASL training templates.

Orientation-aware distinctions, corpus collection/calibration, lessons, motion signs, two-hand synchronization, ML/cloud and feature indexing remain separate future work.
