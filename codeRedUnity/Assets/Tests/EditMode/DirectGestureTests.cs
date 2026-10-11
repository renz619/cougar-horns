using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HandPoseRecognition.Editor;

namespace HandPoseRecognition.Tests
{
    public class DirectGestureTests
    {
        static List<DirectGesture> Run(HandPose shape, int count, float dt, Func<int, Vector3> palm,
            Func<int, float> pitch = null, DirectGestureRecognizer recognizer = null, double startTime = 0)
        {
            recognizer = recognizer ?? new DirectGestureRecognizer();
            var results = new List<DirectGesture>();
            for (int i = 0; i < count; i++)
            {
                var result = recognizer.Update(new DirectGestureSample(startTime + i * dt, palm(i), pitch?.Invoke(i) ?? 0, shape), new DirectGestureSettings());
                if (result != DirectGesture.None) results.Add(result);
            }
            return results;
        }

        [Test] public void ThankYouRequiresOpenHandNearCameraMovingOutward()
        {
            CollectionAssert.AreEqual(new[] { DirectGesture.ThankYou }, Run(HandPose.Open, 16, .05f, i => new Vector3(0, -.1f, .12f + .025f * i)));
            Assert.IsEmpty(Run(HandPose.Fist, 16, .05f, i => new Vector3(0, 0, .12f + .025f * i)));
            Assert.IsEmpty(Run(HandPose.Open, 16, .05f, i => new Vector3(0, 0, .6f + .025f * i)));
            Assert.IsEmpty(Run(HandPose.Open, 16, .05f, i => new Vector3(0, 0, .48f - .025f * i)));
            Assert.IsEmpty(Run(HandPose.Open, 16, .05f, i => new Vector3(.025f * i, 0, .12f + .025f * i)));
        }

        [TestCase(1)] [TestCase(-1)]
        public void PleaseAcceptsBothCircleDirections(int direction)
        {
            CollectionAssert.AreEqual(new[] { DirectGesture.Please }, Run(HandPose.Open, 61, .04f,
                i => new Vector3(.12f * Mathf.Cos(direction * i * Mathf.PI / 30), .12f * Mathf.Sin(direction * i * Mathf.PI / 30), .4f)));
        }

        [Test] public void PartialCircleZigzagDepthMovementAndFistCircleAreRejected()
        {
            Assert.IsEmpty(Run(HandPose.Open, 31, .04f, i => new Vector3(.12f * Mathf.Cos(i * Mathf.PI / 30), .12f * Mathf.Sin(i * Mathf.PI / 30), .4f)));
            Assert.IsEmpty(Run(HandPose.Open, 61, .04f, i => new Vector3(.12f * Mathf.Cos(i * Mathf.PI / 30), .12f * Mathf.Sin(i * Mathf.PI / 30), .4f + .2f * Mathf.Sin(i * Mathf.PI / 30))));
            Assert.IsEmpty(Run(HandPose.Fist, 61, .04f, i => new Vector3(.12f * Mathf.Cos(i * Mathf.PI / 30), .12f * Mathf.Sin(i * Mathf.PI / 30), .4f)));
            Assert.IsEmpty(Run(HandPose.Open, 60, .04f, i => new Vector3(.12f * Mathf.Sin(i * .2f), 0, .4f)));
        }

        [TestCase(1)] [TestCase(-1)]
        public void YesIsFistNodReturningToItsStartingAngle(int direction)
        {
            CollectionAssert.AreEqual(new[] { DirectGesture.Yes }, Run(HandPose.Fist, 41, .025f,
                i => new Vector3(0, -.1f, .4f), i => direction * 40f * Mathf.Sin(i * Mathf.PI / 40)));
        }

        [Test] public void StaticFistOneWayTiltAndOpenHandNodAreRejected()
        {
            Assert.IsEmpty(Run(HandPose.Fist, 41, .025f, i => new Vector3(0, 0, .4f)));
            Assert.IsEmpty(Run(HandPose.Fist, 41, .025f, i => new Vector3(0, 0, .4f), i => i));
            Assert.IsEmpty(Run(HandPose.Open, 41, .025f, i => new Vector3(0, 0, .4f), i => 40f * Mathf.Sin(i * Mathf.PI / 40)));
        }

        [Test] public void StationaryOpenHandAndSmallJitterDoNotDetect()
        {
            Assert.IsEmpty(Run(HandPose.Open, 100, .02f, i => new Vector3(0, 0, .3f)));
            Assert.IsEmpty(Run(HandPose.Open, 100, .02f, i => new Vector3(.01f * Mathf.Sin(i), .01f * Mathf.Cos(i), .3f)));
            Assert.IsEmpty(Run(HandPose.Fist, 100, .02f, i => new Vector3(0, 0, .3f), i => 5 * Mathf.Sin(i)));
        }

        [Test] public void TrackingLossGapShapeChangeAndDuplicateFramesCannotCompleteAnOldAttempt()
        {
            var recognizer = new DirectGestureRecognizer();
            Assert.IsEmpty(Run(HandPose.Open, 5, .05f, i => new Vector3(0, 0, .15f + i * .04f), recognizer: recognizer));
            recognizer.Reset();
            Assert.IsEmpty(Run(HandPose.Open, 10, .05f, i => new Vector3(0, 0, .36f), recognizer: recognizer, startTime: .25));
            recognizer.Reset();
            Run(HandPose.Open, 5, .05f, i => new Vector3(0, 0, .15f + i * .04f), recognizer: recognizer);
            Assert.IsEmpty(Run(HandPose.Open, 10, .05f, i => new Vector3(0, 0, .36f), recognizer: recognizer, startTime: 1));
            recognizer.Reset();
            Run(HandPose.Open, 5, .05f, i => new Vector3(0, 0, .15f + i * .04f), recognizer: recognizer);
            Assert.IsEmpty(Run(HandPose.Fist, 10, .05f, i => new Vector3(0, 0, .36f), recognizer: recognizer, startTime: .25));
            recognizer.Reset();
            Assert.IsEmpty(Run(HandPose.Open, 20, 0, i => new Vector3(0, 0, .15f + i * .04f), recognizer: recognizer));
            Assert.AreEqual(1, recognizer.SampleCount);
        }

        [Test] public void InvalidInputAndSettingsClearTheAttempt()
        {
            var recognizer = new DirectGestureRecognizer();
            var settings = new DirectGestureSettings();
            recognizer.Update(new DirectGestureSample(0, Vector3.forward * .2f, 0, HandPose.Open), settings);
            Assert.AreEqual(DirectGesture.None, recognizer.Update(new DirectGestureSample(.05, new Vector3(float.NaN, 0, 0), 0, HandPose.Open), settings));
            Assert.AreEqual(0, recognizer.SampleCount);
            settings.nodMinimumDegrees = float.NaN;
            Assert.AreEqual(DirectGesture.None, recognizer.Update(new DirectGestureSample(.1, Vector3.forward, 0, HandPose.Fist), settings));
            Assert.AreEqual(0, recognizer.SampleCount);
        }

        [Test] public void SampleConversionUsesCameraAndRigExactlyOnceAndRejectsMissingPalm()
        {
            var joints = HandFrame.CreateUnavailableJoints();
            var hand = FakeHandData.Hand(0);
            AddFinger(joints, 6, hand.index); AddFinger(joints, 11, hand.middle);
            AddFinger(joints, 16, hand.ring); AddFinger(joints, 21, hand.little);
            joints[0] = new JointPose(HandJointId.Wrist, Vector3.zero, Quaternion.identity, true, true);
            joints[1] = new JointPose(HandJointId.Palm, new Vector3(0, .02f, .3f), Quaternion.identity, true, true);
            var rotation = Quaternion.Euler(25, 60, 10);
            var origin = new Vector3(2, 1, 4);
            var rig = Matrix4x4.TRS(origin, rotation, Vector3.one);
            Assert.IsTrue(DirectHandGestureController.TryBuildSample(HandFrameFixtures.Raw(joints), rig, origin, rotation,
                new HandPoseClassifierSettings(), out var sample));
            Assert.Less(Vector3.Distance(joints[1].Position, sample.Palm), 1e-5f);
            Assert.AreEqual(HandPose.Open, sample.Shape);
            joints[1].HasPosition = false;
            Assert.IsFalse(DirectHandGestureController.TryBuildSample(HandFrameFixtures.Raw(joints), rig, origin, rotation,
                new HandPoseClassifierSettings(), out _));
        }

        static void AddFinger(JointPose[] joints, int start, FingerJoints finger)
        {
            var positions = new[] { finger.metacarpal, finger.proximal, finger.intermediate, finger.distal, finger.tip };
            for (int i = 0; i < 5; i++) joints[start + i] = new JointPose((HandJointId)(start + i), positions[i], Quaternion.identity, true, true);
        }

        [TestCase("HandPoseDemo")] [TestCase("HandPoseRecognitionDemo")] [TestCase("HandGestureMotionDemo")]
        public void DemoUsesDirectRulesWithDedicatedCameraLabelAndKeepsBothHandVisualizers(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
            try
            {
                bool savedControllerExists = false;
                foreach (var root in scene.GetRootGameObjects())
                    savedControllerExists |= root.GetComponentInChildren<DirectHandGestureController>(true) != null;
                Assert.IsTrue(savedControllerExists, "Direct gesture controller must be wired in the saved scene.");
                DirectGestureDemoSetup.Ensure(scene);
                DirectHandGestureController controller = null;
                int controllers = 0, visualizers = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    controllers += root.GetComponentsInChildren<DirectHandGestureController>(true).Length;
                    visualizers += root.GetComponentsInChildren<HandJointVisualizer>(true).Length;
                    if (controller == null) controller = root.GetComponentInChildren<DirectHandGestureController>(true);
                    foreach (var old in root.GetComponentsInChildren<HandGestureController>(true)) Assert.IsFalse(old.enabled);
                }
                Assert.AreEqual(1, controllers); Assert.AreEqual(2, visualizers);
                Assert.IsTrue(controller.enabled);
                var serialized = new SerializedObject(controller);
                var hand = (QuestHandAdapter)serialized.FindProperty("m_Adapter").objectReferenceValue;
                Assert.AreEqual(HandSide.Right, hand.HandSide);
                Assert.AreEqual("Camera Offset", serialized.FindProperty("m_TrackingSpace").objectReferenceValue.name);
                Assert.AreEqual("Main Camera", serialized.FindProperty("m_Head").objectReferenceValue.name);
                var label = (TMP_Text)serialized.FindProperty("m_Label").objectReferenceValue;
                Assert.IsNotNull(label);
                Assert.AreNotSame(new SerializedObject(controller.GetComponent<HandPoseDisplay>()).FindProperty("m_Label").objectReferenceValue, label);
                Assert.AreEqual(70, serialized.FindProperty("m_Settings.extendedMaxCurlDegrees").floatValue);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
