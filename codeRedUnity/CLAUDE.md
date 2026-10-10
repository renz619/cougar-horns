# Project: Unity Quest 2 Hand Pose Prototype

## Working rules
- Verify any XR Hands API usage against the installed package source in
  Library/PackageCache before using it. Never invent methods.
- Only implement what the current version asks for. Do not build ahead.
- Work one script at a time and explain where each one fits.
- Keep core logic in plain C# functions so it can be tested without a headset.
- Unity version: 6000.0.34f1. Mac is for code and tests; hand tracking is
  tested by a teammate on Windows with Quest Link.

## Current version: 0.1 (Fist vs Open)
# Unity Quest 2 Hand Pose Prototype

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
0.1 Fist vs Open (rule-based, VR text feedback)
0.2 More static gestures (thumbs-up, pointing, a few ASL handshapes)
0.3 Pose recording (HandFrame, HandPoseRecorder, HandPoseData)
0.4 Template matching (normalization, pose database, shape recognition)
0.5 ASL learning environment (lessons, feedback, motion, two hands)