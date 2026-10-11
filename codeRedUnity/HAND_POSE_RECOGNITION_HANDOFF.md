# Hand pose recognition: implementation handoff for steps 2 and 3

Prepared 2026-10-11 from the implemented foundation and the user's next-work request.

## Implementation status — 2026-10-11

**The assigned software implementation is complete.** Steps 2, 3A and 3B now provide meaningful pure matching, bounded canonical recording, explicit reviewed persistence, stable timing/events and an opt-in template display/demo. The requirements below remain the implementation contract and historical starting point.

- Phase 1: `RecognitionResult` / `ShapeRecognizer` implemented with weighted Euclidean scoring, explicit no-score failures, inclusive thresholds and ordinal ID/stable sample ties.
- Phase 2: frozen, owned `PoseRecordingSession` and explicit `HandPoseRecorder` capture implemented with sample/attempt/time bounds, rejection reports and sequence deduplication.
- Phase 3: editor review/selection and explicit create/replace/append implemented with prospective validation, Undo, save/reimport/reload and domain/Play Mode review retention. Pending SessionState review survives editor transitions, but must be explicitly saved before closing Unity.
- Phase 4: `RecognitionSession` / `RecognitionController` and additive optional display integration implemented; LegacyRules remains the default, template confirmation is separate from geometric observations, and loss clears immediately.
- Phase 5: Unity-created `HandPoseRecognitionDemo.unity`, setup instructions, persistence/Play Mode regression and measured profiling evidence delivered. The original demo/tuning remains untouched.

Current baseline **294/294 passed**; matcher/recorder phase **343/343 passed**; final full Unity suite **392/392 passed**, including actual Play Mode component integration, retained-review transitions and allocation controls. XML and complete logs are archived under `Verification/HandPoseRecognition/`. Native headless OpenXR initialization errors and licensing startup diagnostics are documented separately from C# compilation and synthetic test results.

See [HAND_POSE_RECOGNITION_SETUP.md](HAND_POSE_RECOGNITION_SETUP.md) for every changed file/API, timing/save lifecycle, exact Inspector/Quest Link workflow, result evidence and measured allocation/timing table. **Hardware verification is still pending; no Quest Link pass or ASL accuracy is claimed.**

## Assignment and starting point

Implement the following in dependency order:

1. **Conversation step 2:** a stateless `ShapeRecognizer` and explicit `RecognitionResult`, verified with canonical synthetic samples.
2. **Conversation step 3:** a bounded real-sample recorder with editor review/save, then a controller with stable timing and an opt-in template display path.

These numbers refer to the conversation's next-work list. They do not refer to acquisition and normalization phases in the original foundation handoff. Conversation step 1, the headset regression check, remains a hardware verification task. It has not been reported complete. Continue independent implementation and synthetic verification without inventing a hardware pass; report device checks separately.

Read `CLAUDE.md`, applicable `AGENTS.md`, [HAND_POSE_FOUNDATION_SETUP.md](HAND_POSE_FOUNDATION_SETUP.md), and the frozen contracts in [HAND_POSE_SCALABILITY_HANDOFF.md](HAND_POSE_SCALABILITY_HANDOFF.md). This handoff supersedes their historical exclusions of matching, recording and controller work for this assignment. Preserve all compatibility protections. When implementation is requested, report progress after each phase and proceed through the assigned work; the older thumbs-up approval/stop workflow is not an additional gate. Updating this handoff has not implemented these features.

The foundation already supplies:

| Existing API | Use |
| --- | --- |
| `IHandFrameSource.TryGetLatestFrame(out HandFrame)` | Coherent immutable raw acquisition; true includes untracked status. |
| `HandFrame.SequenceId`, `TimestampSeconds`, `HandSide` | Deduplication, session-clock freshness, side selection. Sequence scope is one source lifetime. |
| `HandPoseNormalizer.TryNormalizeFrame(raw, out canonical, out reason)` | Shared normalization 1; exactly once per new raw acquisition in each consuming pipeline. |
| `PoseSample.TryCreate(canonical, weights, out sample, out report)` | Validated owned canonical recording conversion. |
| `HandPoseData.SetDefinition(...)`, `TryValidate(...)` | Copied authoring inputs, explicit template validation. |
| `PoseDatabase.TryBuildSnapshot(out snapshot, out report)` | Atomic validated build, no usable snapshot on failure. |
| `snapshot.GetCandidates(side)`, `TryGetPose(id, out template)` | Cached immutable side buckets and ordinal ID lookup. |
| `PoseTemplate.JointWeights`, `Samples`, `MaxError` | Read-only comparison data; `PoseSample.TryGetJoint` avoids copying arrays in the match loop. |

Current versions: Unity 6000.0.34f1, XR Hands 1.5.1, OpenXR 1.14.0, Test Framework 1.4.5. The last full foundation run passed **294/294**, including the Play Mode allocation probe; archived evidence is under `Verification/HandPoseScalability/`. Rerun the current baseline before edits; historical counts are evidence, not a guarantee of the new baseline.

`Assets/HandPoses/Synthetic/SyntheticDatabase.asset` contains one right-hand fixture, `synthetic.fixture.right`. It is not a real open-hand/ASL training template and must stay clearly labeled synthetic. Use new test-owned geometry for deterministic matches and temporary persistence assets. Real-template labels require actual recordings and separate accuracy evidence.

## Compatibility and scope

- Keep layout 1's 26 slots, side-specific samples, sample/template/database schema 1 and normalization 1 unchanged. An incompatible data change needs an explicit migration/version decision; do not casually revise contracts to simplify matching.
- Preserve `HandPose` enum values and protected classifier bodies: `Classify`, `Tally`, `FingerCurlDegrees`, `ClassifyThumb`, `ThumbTipDistanceRatio`. Existing tests remain unedited; additive fixtures/tests are allowed.
- Preserve serialized field names, existing `.meta` GUIDs, scene references, all classifier defaults, and saved demo values: curl 70/160, thumb 0.75/0.45, spread 0.10/0.25, world-up angle 35 degrees. Keep the original demo's right-hand selection and Camera Offset reference.
- Keep XR Hands API access in QuestHandAdapter. Recorder/controller consume IHandFrameSource and Unity clock/integration adapters. Matching, capture-selection state and timing policy must be testable as plain C# with injected time and synthetic sources.
- Use the existing Runtime, Editor and EditMode test assemblies. Add Unity-generated `.meta` files for new Assets files/folders. Keep AssetDatabase/UnityEditor calls in Editor code. Add no packages just for this work.
- Orientation-aware recognition, lessons, motion signs, two-hand synchronization, cloud/ML and corpus indexing remain deferred. Canonical shape cannot establish world-up thumbs-up or orientation-dependent ASL labels. The old backend retains its existing orientation behavior.

## Step 2: ShapeRecognizer and result contract

### Public boundary

Provide a plain stateless matcher with a documented surface equivalent to:

```csharp
RecognitionResult Recognize(
    HandFrame canonical,
    PoseDatabaseSnapshot catalog,
    double nowSeconds,
    double maxFrameAgeSeconds);
```

The explicit clock/freshness inputs keep this helper pure. It does not read hardware, normalize input, rebuild catalogs, mutate assets, own dwell/cooldown, or emit events. The caller normalizes raw input once. Validate finite nonnegative `nowSeconds` and a finite positive freshness bound; reject timestamps ahead of now rather than clamping a negative age. The freshness boundary is inclusive: age <= maxFrameAgeSeconds is usable. A startup/reload caller builds the catalog separately.

`RecognitionResult` should be an immutable value with safe unusable defaults. Include `PoseId`, `Error` (prefer double for accumulated numerical range), `HasScore`, `IsMatch`, `HandSide`, frame timestamp/sequence, and an explicit status/reason. A sample index is useful for deterministic debugging. Do not implicitly map string IDs to the legacy enum. Suggested statuses: Unavailable/default, InvalidSettings, InvalidFrame, Untracked, Stale, UnsupportedFrame, CatalogUnavailable, NoCandidates, NoEligibleSamples, AboveThreshold, Matched. Equivalent names are acceptable if the semantics are documented and tested.

- Invalid/untracked/stale/unsupported input, missing catalog, empty same-side candidates and no eligible samples have `HasScore = false`, `IsMatch = false`, and no matched PoseId. A default Error value cannot be interpreted as a valid score.
- AboveThreshold has a valid finite best error (`HasScore = true`) but `IsMatch = false`; expose any diagnostic best-candidate ID separately from the matched PoseId.
- Matched has a known PoseId, valid finite error and `IsMatch = true`.
- Controller-side normalization failure is a separate observation failure. Surface its actual reason without inventing a match/score or sending raw input to the matcher.

### Weighted comparison

Require tracked, usable, known-side `CanonicalHandSpace`, layout 1 and normalization 1 input. Filter candidates by side/layout/normalization. For each template/sample:

```text
I = all template joints with weight w_i > 0
Require finite available positions in sample P and live frame Q for every i in I.
E(P,Q) = sum(w_i * EuclideanDistance(P_i,Q_i)) / sum(w_i), i in I
E_pose(Q) = minimum eligible sample error for that pose
```

Use double intermediate differences, distances and accumulation, while guarding finite results and positive denominator. Use Euclidean distance, not squared distance. Zero-weight joints contribute nothing and may be unavailable. Missing positive-weight joints makes that comparison ineligible; do not shorten the denominator, substitute placeholders or impute geometry. Optional joint rotations do not affect this position matcher.

Choose the globally lowest eligible pose error, breaking exact ties by ordinal PoseId and then stable sample index. Match only when that winner's error <= its own MaxError, inclusively. Do not silently choose a higher-error pose merely because its threshold is wider. Return AboveThreshold with the best score if the winner fails its threshold. Do not call distance a probability/confidence, introduce an uncalibrated ambiguity margin, or silently fall back to legacy classification.

Iterate immutable cached candidates/samples directly. No LINQ materialization, array copies, asset lookups, validation rebuild or normalization per candidate. Template weights are already validated at build; numerical checks during comparison still protect computed terms. Stateless matching is O(S * 26). Feature indexes/backends await measured corpus needs.

### Step 2 tests and completion

Add tests for exact synthetic matches; a hand-computed weighted Euclidean score; zero-weight exclusion; multiple samples/minimum selection; inclusive threshold just inside/on/outside; a best candidate above its own threshold; ordinal ID and stable sample ties; side/version filtering; each input/freshness failure; no candidates/eligible samples; required-joint loss; position-only samples; finite extreme-coordinate/weight arithmetic; and unchanged retained inputs/catalogs. Test fresh-boundary, future timestamp and bad clock/settings values using injected time.

Complete step 2 when the pure matcher produces meaningful results against canonical fixtures, the baseline and added tests pass, and actual result counts/XML/compiler/Console evidence are recorded. UI and real-data accuracy are step 3/hardware evidence; do not invent a live match to complete this phase.

## Step 3A: bounded recorder and sample review/save

### Capture contract

Provide a plain `PoseRecordingSession` (or equivalent) plus a thin `HandPoseRecorder` component and minimal Editor review/save tooling. Runtime recording collects data; it does not classify, train, write files/assets or invoke lessons.

At Begin, copy and freeze the intended PoseId, display name, side, 26 weights, MaxError and template sample budget. Validate settings immediately. Freeze the selected source identity as well; source replacement cannot share its old sequence scope. Do not let Inspector edits silently change the meaning of an in-progress capture.

Expose explicit Begin, Stop, Cancel and clear/reset behavior, recording state, accepted/rejected counts, last rejection reason, and completion reason. Suggested states: Idle, Recording, ReadyForReview, Canceled. Completion reasons distinguish manual stop, accepted-sample limit, attempt budget, timeout and source replacement. Zero accepted samples is a valid stopped session but cannot be saved as a usable template.

Initial configurable capture limits (provisional storage/UX values):

| Setting | Initial value/meaning |
| --- | --- |
| Maximum accepted samples | 32, and never above the destination template's MaxSamplesPerPose. |
| Maximum distinct-frame attempts | 256, including rejected updates. |
| Maximum recording duration | 10 seconds from Begin on the same monotonic clock. |
| Minimum accepted-sample interval | 0.1 seconds; not a claim that adjacent samples are independent evidence. |
| Maximum frame age | 0.25 seconds, finite positive, configurable and subject to hardware verification. |

Bound both storage and time/attempts, including sessions with no updates or only tracking failures. Validate all limits; do not silently clamp malformed configuration. On reaching a limit, stop with a clear report. Do not silently truncate caller samples or overwrite accepted samples in a ring buffer.

For each current raw acquisition, deduplicate by source lifetime and sequence before recording. Repeated reads cannot count as new attempts or samples. Require the frozen side, raw space/versions, finite valid timestamp, freshness, tracking and normalization anchors. Normalize once, then call `PoseSample.TryCreate` using the frozen weights; missing positive-weight positions rejects that acquisition. Respect the interval between accepted frames, and retain owned canonical samples/provenance in capture order. Samples/weights exposed for review must not let consumers mutate the session's backing storage.

Transient tracking/normalization failure rejects that update with a reason; recovery can continue within the bounds. Cancellation discards the pending recording explicitly. Source replacement terminates the session with a reason and requires a new Begin. Count/report interval-skipped and invalid acquisitions consistently. No background recording begins automatically when entering Play Mode.

### Minimal Editor UX and persistence

Provide a small window or custom inspector with source/side, definition fields, Begin/Stop/Cancel, bounds, accepted/rejected counts, per-sample index/provenance, and explicit selection/removal. Record a bounded candidate set, then let the user keep representative samples. A simple list with keep/remove is sufficient; clustering or a full timeline is out of scope.

Save only after recording stops and the user explicitly chooses a nonempty valid selection. Create a new template asset under a chosen `Assets/HandPoses/` folder, using a unique path by default. Require an explicit editor action/Undo support to replace or append to an existing template; preserve its PoseId and validate side/weights/version/sample-budget compatibility. Existing samples plus appended selections cannot exceed the budget. Do not silently change identity, renormalize canonical data, truncate samples, or overwrite synthetic fixtures.

Use the existing conversion/validation path and `HandPoseData.SetDefinition`; validate the complete proposed definition before mutating a destination asset. For catalog assignment/update, validate a prospective complete reference set before applying it; duplicate IDs must fail without partially updating the saved catalog. Asset edits require an explicit snapshot rebuild, leaving any previously published snapshot unchanged. Save/reimport/reload and validate again, reporting errors with the actual asset/sample/joint path.

Keep pending sample data safe across leaving Play Mode/domain reload: either persist owned serialized review data in editor tooling and restore it, or require/save it through a tested explicit workflow before exit. Do not present a buffer as safely retained if it disappears on the usual editor transition. Do not automatically write capture data to real template assets on Stop/disable/Play Mode exit. Document and test the chosen review-data lifecycle.

### Recorder tests and completion

Test Begin validation/frozen input ownership; side/source changes; repeated sequence suppression; interval/freshness boundaries; bad/future timestamps; tracked and missing-anchor/weighted-joint failures; loss/recovery; accepted/attempt/duration limits including no-update timeout; Stop/Cancel/zero-sample behavior; sample retention and review ownership; and selection/save budget handling. Test create/save/unload/reload of temporary assets, append/update identity preservation, duplicate-catalog rejection without partial writes, explicit rebuild immutability, and the chosen Play Mode/domain-reload retention workflow. Delete only test-owned temporary assets.

Complete this phase when real acquisitions can produce bounded, reviewable samples through the shared normalizer and persist/reload a validated template/catalog. Synthetic tests prove the workflow; real-recording quality requires headset evidence.

## Step 3B: controller timing and opt-in display integration

Provide a `RecognitionController` integration component backed by a plain timing/state helper (for example `RecognitionSession`). Inject clock/source data into the helper for tests. The component reads one selected source, builds the database once on enable/start or explicit rebuild, and owns backend selection, evaluation interval, freshness, dwell, clearing and event cooldown.

Use explicit LegacyRules/Template backend selection, defaulting to LegacyRules. With no controller assigned, HandPoseDisplay's existing path behaves exactly as before. For this milestone, legacy classifier execution and tuned m_Settings may remain in HandPoseDisplay; the controller's selected backend tells the display which path to use. Do not duplicate legacy thresholds into new defaults or migrate serialized settings just to wire the controller. If later moving that execution into the controller, preserve the same settings instance/values and existing public test behavior.

Add an optional controller reference to HandPoseDisplay. In Template mode, it displays the controller's confirmed template DisplayName/PoseId and explicit status/error availability, while preserving useful raw finger/spread/orientation diagnostics. Keep `currentPose`, legacy labels and static formatting helpers compatible: template string IDs do not manufacture HandPose enum values; expose additive template-result/ID accessors, and report legacy currentPose as Unknown in Template mode. Ensure only one path writes the assigned TMP label. Selecting Template mode with an unavailable/disabled controller/catalog must show unavailable/Unknown with a reason, never silently use legacy results. Retain the last explicitly selected Template mode if its controller is disabled, destroyed or disconnected; a null-reference fallback must not switch backends. The untouched scene with no controller starts in LegacyRules, and returning to it after a Template selection requires an explicit mode/reset action.

Initial configurable timing settings (provisional, not hardware-calibrated): maximum age 0.25 seconds, minimum evaluation interval 0.1 seconds, confirmation dwell 0.2 seconds, event cooldown 0.5 seconds. Allow nonnegative finite interval/dwell/cooldown and finite positive freshness; invalid settings disable the template path with a clear status. Define and test boundaries explicitly.

- Normalize each new sequence at most once, cache its canonical/result data, and never recompute the same sequence for every display refresh. Newer skipped sequences are not queued indefinitely; evaluate the latest eligible acquisition at the next permitted evaluation time.
- Inspect availability, tracking and age even while rate-limited or reading a repeated sequence. Disable/shutdown/loss/staleness immediately clears confirmation and pending dwell; it cannot leave a prior valid label or emit a success.
- Start dwell on the first eligible match for a PoseId. Confirm only after continued eligible matches on distinct acquisitions spanning the dwell, with no tracking/freshness/normalization failure. Re-reading one frame for 0.2 seconds is not confirmation evidence. A different pose or above-threshold/no-match observation resets the pending dwell.
- Separate `LatestObservation` (the stateless matcher result) from confirmation/pending/controller status. Before confirmation show Unknown/holding status, without disguising a geometric Matched observation as a new scored result. For this first controller use immediate release on a nonmatch; an additional hysteresis grace band/release delay remains a future explicit policy, not a hidden behavior.
- Emit a confirmed-pose event once per newly confirmed hold. Continuing to hold a pose does not repeatedly emit as cooldown expires. Releasing and re-confirming is a new hold; suppress its event while cooldown is active, then emit once if that new hold still has fresh supporting observations. Cooldown controls events, not the truth of the current match or the loss display.
- Source, backend or database snapshot replacement clears cached sequences, dwell and confirmation. Disable unsubscribes and clears current state. Document whether cooldown is reset on re-enable; use a deterministic policy and test it. Catalog startup failure leaves Template mode unavailable. An explicit failed reload may retain a previous snapshot only if the caller deliberately opts in and surfaces the failed report.

Use result events with PoseId and source provenance for future consumers. No lesson behavior is implemented. The matcher remains stateless; the recorder remains independent. If controller and recorder later share normalized acquisitions, ownership/deduplication must remain explicit; do not alter the frozen source contract to add live mutable buffers.

### Unity setup deliverable

Leave `HandPoseDemo.unity` and its saved tuning untouched. Provide an editor-created opt-in demo variant (copy the scene or add a documented test setup without regenerating the original) with QuestHandAdapter, the controller/database reference, optional recorder, and the display's controller reference. Default backend remains LegacyRules. Provide exact Inspector wiring and a deliberate switch to Template mode.

Document an actual sequence: connect Quest Link -> run the preserved demo -> Begin a labeled bounded capture -> Stop and review -> explicitly save/reload the template -> assign/rebuild a catalog -> select Template backend -> hold the recorded shape -> inspect score/status/confirmation -> hide/recover the hand -> switch back to LegacyRules. Distinguish real recorded templates from the supplied synthetic fixture. No validated ASL accuracy is implied by a successful recording/match demonstration.

### Controller verification and completion

Use injected-clock tests for duplicate frames, evaluation interval, inclusive freshness, distinct-acquisition dwell, pose changes, loss/staleness while rate-limited, immediate release, cooldown/re-hold behavior, event counts, disable/re-enable, source/backend/snapshot changes and catalog failure/reload policy. Add display tests for unchanged default legacy behavior, Template name/PoseId/status/no-score formatting, pending confirmation, unavailable controller and mode switching. Existing display tests remain untouched.

Run the full Unity suite and Play Mode integration after all phases. Record actual Unity version/counts/XML, C# compilation and Console/native initialization errors separately. Profile the pure matcher, normalization, controller updates and recorder separately after warm-up with fixed source/candidate/sample counts; report allocation events or measured bytes accurately, plus timings when actually measured. Previous acquisition cost was 2 allocation events per Dynamic capture and 3 more for normalization; repeated reads were 0. Do not pool arrays still owned by retained frames/samples/catalogs. Match-loop allocation claims require a visible positive measurement control.

## Proposed files and implementation phases

| Phase | Files (equivalent names acceptable) | Deliverable |
| --- | --- | --- |
| 1 / step 2 | `RecognitionResult.cs`, `RecognitionStatus.cs`, `ShapeRecognizer.cs`; additive matcher tests | Pure weighted matching and explicit failure/score semantics. |
| 2 / step 3A | `PoseRecordingSession.cs`, recorder settings/status data, `HandPoseRecorder.cs`; additive recording tests | Bounded owned capture, deduplication and reviewable samples. |
| 3 / step 3A | `Editor/PoseRecorderWindow.cs` or equivalent, small extensions to existing authoring tool; persistence tests | Minimal selection, safe explicit save/update/rebuild, tested Play Mode retention. |
| 4 / step 3B | `RecognitionSession.cs`, `RecognitionController.cs`, timing settings/backend enum; additive display changes/tests | Explicit backend, stable confirmation/events and opt-in visible template results. |
| 5 | Opt-in demo/setup documentation, regression/persistence/Play Mode evidence | Review full diff, report measured performance and remaining device checks. |

Implement dependencies sequentially and explain each file's role. Do not replace the working foundation, rewrite old test methods, add fake-success matcher outputs, implement lesson logic, or tune the original scene to fit the new fixtures.

## Completion report and remaining hardware work

Report changed files/public APIs, matcher score/status/tie behavior, recorder bounds/review/save lifecycle, controller confirmation/cooldown policy, exact Unity setup, actual tests/XML/Console evidence and actual allocation/timing observations. Update implementation status only for completed work and record any remaining verification explicitly.

Device checks remain: all five digits and eight old poses; tracking loss/recovery; nonidentity Camera Offset/XR Origin world-up conversion exactly once; real raw/recorded frame side and availability; bounded capture and save/reload; actual Template confirmation/loss/cooldown; LegacyRules restoration without changing saved thresholds; and target-player CPU/allocation profiling. Do not report headset testing or calibrated recognition accuracy from synthetic tests.

The assignment is complete in software when meaningful template matching, bounded canonical recording, safe reviewed persistence, controller timing/events and the opt-in display path are implemented and regression tests pass with setup/evidence documented. Hardware verification is complete only after real Quest Link results are recorded. The following handoff can then address corpus collection/calibration and orientation-aware distinctions; lessons/motion/two-hand work still require separate scope.
