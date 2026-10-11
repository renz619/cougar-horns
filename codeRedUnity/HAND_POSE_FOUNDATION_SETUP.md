# Hand pose data foundation: implementation and next handoff

Implemented 2026-10-11 against `HAND_POSE_SCALABILITY_HANDOFF.md`. The current demo remains the legacy classifier. This adds acquisition, canonical sample data and a validated catalog; matcher, recorder, controller and lessons remain deferred.

The next implementation scope is now specified in [HAND_POSE_RECOGNITION_HANDOFF.md](HAND_POSE_RECOGNITION_HANDOFF.md): step 2 is the matcher/result contract; step 3 is bounded recording, editor review/save and controller/display integration. These are assignments, not completed features. The historical implementation/test evidence below is unchanged; Quest Link verification remains unperformed.

## Files and public API decisions

All runtime code remains in `HandPose.Runtime`, namespace `HandPoseRecognition`. No packages or runtime assembly boundaries were added.

| File under `Assets/Scripts/HandPose/` | Role |
| --- | --- |
| `HandJointId.cs` | Explicit, frozen layout-1 slots 0–25; Invalid = -1. |
| `HandSide.cs`, `HandFrameSpace.cs` | Project enums with unusable Unknown defaults; XR enums stay at the adapter boundary. |
| `JointPose.cs` | Serializable value with independent position/rotation flags; internal finite-number and quaternion helpers. |
| `HandFrame.cs` | Sealed owned snapshot; constructor validates metadata/layout, copies arrays and sanitizes joint availability. |
| `IHandFrameSource.cs` | `HandSide` plus `TryGetLatestFrame`; true means a snapshot exists, including loss status. |
| `QuestHandAdapter.cs` (changed) | Explicit XR mapping, one read per joint per Dynamic update, reusable scratch storage, acquisition sequence/time, loss/disable/shutdown invalidation. |
| `HandFrameLegacyProjection.cs` | Copies one acquisition's positions into old digit structs without requiring wrist/palm or rotations. |
| `HandPoseNormalizer.cs` | Pure normalization-1 conversion; raw-only, anchor-based, no mirroring or fallback basis. |
| `PoseSample.cs` | Owned serializable canonical joints and provenance; shared `TryCreate` conversion; copied joint access. |
| `HandPoseData.cs` | Template authoring asset; stable string ID, side, samples, 26 weights, threshold and storage budget. |
| `PoseDataValidation.cs` | Shared validation and actionable `ValidationReport.Errors`; rotation unavailability does not disqualify positional samples. |
| `PoseTemplate.cs` | Copied immutable runtime template with read-only weights/samples, no asset reference. |
| `PoseDatabase.cs` | Explicit asset catalog; `TryBuildSnapshot` validates all children before publishing anything. |
| `PoseDatabaseSnapshot.cs` | Ordinal ID lookup, cached side buckets, deterministic ordinal candidate order and stable sample order. |
| `Editor/PoseDatabaseAuthoring.cs` | Synthetic fixture creation, explicit validation menu and database Inspector button; all AssetDatabase calls remain in the existing Editor assembly. |

`HandFrame` construction throws `ArgumentException` for malformed counts/order/versions, invalid times/sides or nonfinite orientation metadata. Ordinary missing tracking/joints does not throw. Successful provider reads are checked numerically: bad positions become unavailable, and finite nonzero quaternions normalize; failed reads never create valid identity rotations. Untracked frames expose no available joints. `TryGetJoint` returns position availability and still supplies the value so rotation availability can be inspected separately. `CopyJoints`, `CopySamples` and `CopyJointWeights` return owned copies.

Acquisition uses `Time.realtimeSinceStartupAsDouble`, Unity's monotonic real-time session clock, sampled inside the callback/status publication. It is not wall time or a hardware timestamp. Sequence scope is a component lifetime; Dynamic updates and published invalidations advance it, reads do not. BeforeRender never publishes. Consumers must apply their own freshness policy. Disable publishes an untracked status; shutdown is detected on Update or a source read, so reads cannot return stale tracked geometry.

Raw positions remain tracking-space meters. Canonical positions are unitless palm-width coordinates. Normalization 1 requires Wrist, IndexProximal, MiddleProximal and LittleProximal, rejects width/projected-forward lengths below `1e-4` meters and cross-direction length below `1e-6`, and checks intermediate/output finiteness. It transforms rotations by inverse basis once, preserves side/time/sequence, and carries `TrackingSpaceToWorld` strictly as original tracking-space metadata. Separate left/right templates remain required.

Template/sample/database schemas and joint-layout/normalization versions are 1. Weights default to one for all 26 joints; every positive-weight position is mandatory in every sample. MaxError defaults to an uncalibrated inclusive `0.1` normalized-distance threshold. Maximum samples defaults to 32 and is configurable per asset; excess input fails without truncation. `SetDefinition` copies authoring inputs; explicit validation is still required after any authoring change. Sample creation requires a usable canonical frame. Inspector-authored data passes the same validation on catalog build. Bad optional rotations are marked unavailable in runtime copies.

A build returns a complete snapshot or false with a null snapshot and actionable paths. Empty supported catalogs succeed. Duplicate IDs fail across sides; case differences are distinct ordinal IDs. Asset edits require explicit rebuild and cannot mutate published snapshots. There is no hardware access, asset discovery, validation or indexing in candidate retrieval.

## Unity authoring and saved synthetic fixtures

1. Keep the existing `Hand Pose Recognition` GameObject and its QuestHandAdapter, HandPoseDisplay and HandJointVisualizer references. No new recognizer component is needed. The demo still selects Right and uses Camera Offset. Its saved thresholds remain curl 70/160, thumb 0.75/0.45, spread 0.10/0.25 and world-up angle 35 degrees. Script defaults and serialized field names are preserved.
2. The provided `Assets/HandPoses/Synthetic/SyntheticRight.asset` contains one canonical right-hand sample, ID `synthetic.fixture.right`, displayed as **SYNTHETIC right-hand fixture (not ASL training data)**. `SyntheticDatabase.asset` references it. Both were generated, saved and reimported by Unity, with matching Unity-generated file/folder `.meta` files. This geometry exercises data contracts; it is not validated ASL training data.
3. Select `SyntheticDatabase.asset` and press **Build and validate runtime snapshot** in the Inspector, or use **Tools > Hand Pose > Validate Selected Catalog**. Expected report: 1 pose, 1 sample; Left 0, Right 1; schema/layout/normalization 1. Failures include the database/template/sample/joint path in the Console.
4. **Tools > Hand Pose > Create Synthetic Catalog** makes another independent synthetic catalog through raw frame → shared normalizer → `PoseSample.TryCreate`. Generated asset paths and repeat-call IDs are unique. This tool does not overwrite the existing fixtures.
5. For authoring your own definitions, **Assets > Create > Hand Pose > Template** and **Database** expose the assets. Assign explicit template references to the database. An empty new template intentionally fails validation until it has a stable ID, display name, known side and validated samples. Build explicitly after edits; merely assigning Inspector references does not compile a runtime snapshot.
6. Real samples will later use a bounded recorder: retain a current raw frame once per sequence, normalize once, create a sample with the intended weights, choose representative captures, populate a template via `SetDefinition`, save in editor tooling and explicitly rebuild. The recorder and its selection UX do not exist yet. Orientation-dependent labels cannot be promised by this shape-only pipeline.

## Verification evidence

Unity **6000.0.34f1**, XR Hands **1.5.1**, OpenXR **1.14.0**, Test Framework **1.4.5**. APIs were checked against installed PackageCache source, including XR ID offsets, `TryGetPose`, native storage ownership, success flags and Dynamic/BeforeRender behavior. PackageCache was not edited.

Testing used an isolated copy of Assets/Packages/ProjectSettings in workspace `.verification/HandPoseBaseline`, preserving the user's already-open editor session. The baseline was run before new files were copied into that project.

| Run | Actual result | Archived XML |
| --- | --- | --- |
| Original baseline | 219 passed, 0 failed, 0 skipped | `Verification/HandPoseScalability/baseline.xml` |
| Final complete EditMode suite, including a test that enters/exits Play Mode | 294 passed, 0 failed, 0 skipped | `Verification/HandPoseScalability/editmode.xml` |
| Isolated corrected allocation probe | 1 passed, 0 failed, 0 skipped | `Verification/HandPoseScalability/allocations.xml` |

New tests: `HandFrameFixtures.cs`, `HandFrameTests.cs`, `HandFrameAdapterTests.cs`, `HandPoseNormalizerTests.cs`, `PoseDatabaseTests.cs`, `PoseDatabasePersistenceTests.cs`, `HandFrameAllocationTests.cs`. The existing test assembly definition adds references to the existing Editor assembly, XR Hands and Unity.Collections for real synthetic-provider integration and asset persistence. Existing test methods/helpers are untouched.

Tests cover all 26 slots; structural/numerical failures; caller ownership; tracking loss, failed selected-side/root/joint updates, recovery, disable/shutdown; BeforeRender and repeated-read behavior; missing anchors versus legacy digit validity; all eight existing recognized poses through acquisition; translation/rotation/positive-scale shape invariance; anchor degeneracy/overflow; optional joints/rotations; template identity, weights, budgets and versions; atomic builds, ordinal lookup and deterministic side buckets; old-snapshot immutability; and asset save, unload, reload, joint data and timestamp/sequence persistence. Temporary persistence folders use unique test-owned names and are the only cleanup targets.

Intermediate runs found two new assertion/fixture errors: exact quaternion equality after normalization and an overflow fixture whose original finite coordinates did not actually overflow. Those new tests were corrected and the complete suite rerun. No existing test was changed to obtain a pass.

Compilation and Console/log evidence are separate from test counts: the successful final Unity run compiled without C# errors and had no unexpected managed test LogError/exception failures. The batch startup log contains licensing diagnostics (signature validation/access-token messages) and, on entering headless Play Mode, native OpenXR `XR_ERROR_FORM_FACTOR_UNAVAILABLE`/display initialization diagnostics. The synthetic-provider tests still passed; these are not successful device initialization evidence. The user's interactive Console was not inspected or cleared. Full baseline/final/authoring logs remain at workspace `.verification/baseline-unity.log`, `verified-unity.log`, and `authoring-unity.log`.

The authoring execute-method run exited successfully and reported a saved/reloaded Right catalog with 1 pose and 1 sample, schema/layout/normalization 1. The persistence test separately unloads both test assets before reimport/load, then compares rebuilt data.

The diff review confirmed that the only existing source changes are QuestHandAdapter and the test assembly references. Protected classifier bodies, the HandPose enum, existing test files, display/visualizer code, scene references/tuned values, existing `.meta` GUIDs, project/package settings and unrelated XR samples are unchanged. New Assets files include Unity-generated `.meta` files.

## Measured allocation observations

The Play Mode probe uses Unity's `GC.Alloc` recorder on the current thread, with a visible known-allocation positive control, 128 warm-up iterations and 512 measured synthetic Dynamic updates. The provider reports two tracked hands; one adapter captures its selected Right hand. No automatic hardware provider data is used by the probe.

| Synchronous operation per Dynamic update | Measured managed allocation events |
| --- | --- |
| Synthetic XR subsystem alone | 0 |
| XR subsystem + selected-hand capture | 2 |
| XR subsystem + capture + normalize once | 5 |
| 512 repeated latest-frame reads | 0 total |

Capture owns a new HandFrame and a copied 26-joint array: two allocations. Normalization adds its working array, a new canonical HandFrame and that frame's owned array: three more. Adapter capture scratch storage is reused, but retained snapshot storage never is. Database/sample copies and validation/index allocation occur during authoring/build, not retrieval.

These are event counts, not byte sizes or Quest player performance. An initial Mono `GC.GetAllocatedBytesForCurrentThread` probe returned unusable zeros and was discarded. `GC.Alloc` sample Value measures marker duration; this probe uses its event Count and does not misreport duration as bytes. The installed Test Framework uses allocation-event recording for its own GC assertions. See Unity's [current-thread recorder documentation](https://docs.unity.com/en-us/engine/6000.5/script-reference/unity/profiling/profilerrecorderoptions/collectonlyoncurrentthread). No allocation-free or hardware-throughput claim is made. Byte sizes, CPU time and suitability of this ownership cost must be profiled on the target player before considering a separate caller-owned-buffer optimization.

## Remaining hardware verification

Quest Link checks were not performed. Verify all five digits and existing Open/Fist/Pointing/V/U/Y/L/ThumbsUp behavior using preserved tuned scene values; tracking loss must show Unknown. Inspect selected-side raw frame identity, per-joint availability and sequence progression. Rotate Camera Offset/XR Origin nontrivially: raw joint coordinates must remain tracking space, and the old thumb direction must convert to world exactly once. Confirm recovery and source shutdown on the actual device. Recognition accuracy/calibration, actual device allocations/CPU timing and rig-scale support remain separate from synthetic evidence. Nonuniform or negative rig scale remains unsupported.

## Concrete next milestone: ShapeRecognizer

Read a current raw frame via `IHandFrameSource.TryGetLatestFrame`. Reject untracked/unknown/unsupported or stale input; the future controller supplies an explicit freshness bound. Avoid duplicate sequence capture. Normalize that acquisition once via `HandPoseNormalizer.TryNormalizeFrame`. At startup or explicit reload build a validated `PoseDatabaseSnapshot`; on failure surface the report and disable recognition (a future reload caller may deliberately retain an older good snapshot). Retrieve the cached `GetCandidates(canonical.HandSide)` and implement a stateless weighted Euclidean-distance matcher against their canonical samples.

```text
I = every template joint with w_i > 0
Require finite valid positions in P and Q for every i in I.
E(P,Q) = sum(w_i * EuclideanDistance(P_i,Q_i)) / sum(w_i), i in I
E_pose(Q) = minimum eligible sample error for the pose
```

Filter by side/layout/normalization first. Guard finite terms/accumulation and a positive denominator. Use Euclidean distance, not squared distance; threshold units are normalized palm widths. Missing required joints makes a candidate ineligible, never a zero error or a reduced denominator. Empty/no-eligible catalogs return NoMatch with an explicit reason and no valid score. Choose the lowest eligible error; exact ties use ordinal PoseId then stable sample index. A match requires inclusive `E_pose <= MaxError`. Errors are not probabilities/confidence; any runner-up margin requires calibration later.

A future RecognitionResult carries PoseId, Error, HasScore, IsMatch, side, source timestamp/sequence and status/reason. Distinguish tracking/normalization failure from a valid observation with no matching pose; no-match cannot expose a valid default-zero score. Linear matching costs O(S × 26); candidate filtering helps, and feature indexing should await profiling/corpus evidence.

The future controller owns backend choice, timing, dwell/hysteresis, cooldown and events. Do not merge this catalog with legacy enum recognition or change priority implicitly. Lessons reference PoseId and controller results. Recording/selection UX, orientation-aware recognition, lessons, motion signs, synchronization, cloud and ML remain deferred.
