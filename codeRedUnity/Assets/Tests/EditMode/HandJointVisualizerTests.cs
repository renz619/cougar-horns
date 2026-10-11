using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class HandJointVisualizerTests
    {
        [Test]
        public void TrackedFinger_UsesTrackingSpaceAndMeasuredJointPositions()
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                var hand = FakeHandData.Hand(5f);
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");

                Assert.IsTrue(lines[0].enabled);
                Assert.AreEqual(trackingSpace, lines[0].transform.parent);
                Assert.IsFalse(lines[0].useWorldSpace);
                Assert.AreEqual(hand.index.metacarpal, lines[0].GetPosition(0));
                Assert.AreEqual(hand.index.tip, lines[0].GetPosition(4));
                Assert.AreEqual(trackingSpace.TransformPoint(hand.index.tip),
                    lines[0].transform.TransformPoint(lines[0].GetPosition(4)));
            });
        }

        [Test]
        public void TrackedThumb_UsesFourMeasuredJointsAndSameMaterialInTrackingSpace()
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                var hand = FakeHandData.WithThumb(FakeHandData.Hand(5f), FakeHandData.Thumb(45f, 0f));
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");

                Assert.AreEqual(5, lines.Length);
                var thumb = lines[4];
                Assert.IsTrue(thumb.enabled);
                Assert.AreEqual(trackingSpace, thumb.transform.parent);
                Assert.IsFalse(thumb.useWorldSpace);
                Assert.AreSame(lines[0].sharedMaterial, thumb.sharedMaterial);
                Assert.AreEqual(4, thumb.positionCount);
                Assert.AreEqual(hand.thumb.metacarpal, thumb.GetPosition(0));
                Assert.AreEqual(hand.thumb.proximal, thumb.GetPosition(1));
                Assert.AreEqual(hand.thumb.distal, thumb.GetPosition(2));
                Assert.AreEqual(hand.thumb.tip, thumb.GetPosition(3));
                Assert.AreEqual(trackingSpace.TransformPoint(hand.thumb.tip),
                    thumb.transform.TransformPoint(thumb.GetPosition(3)));
            });
        }

        [Test]
        public void TrackingLoss_HidesPreviouslyVisibleFingers()
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                var hand = FakeHandData.WithThumb(FakeHandData.Hand(5f), FakeHandData.Thumb(45f, 0f));
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");
                foreach (var line in lines)
                    Assert.IsTrue(line.enabled);

                SetField(adapter, "m_CurrentHand", default(HandJointData));
                Invoke(visualizer, "LateUpdate");
                foreach (var line in lines)
                    Assert.IsFalse(line.enabled);
            });
        }

        [Test]
        public void InvalidFinger_HidesOnlyThatFinger()
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                var hand = FakeHandData.WithThumb(FakeHandData.Hand(5f), FakeHandData.Thumb(45f, 0f));
                hand.index.isValid = false;
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");
                Assert.IsFalse(lines[0].enabled);
                Assert.IsTrue(lines[1].enabled);
                Assert.IsTrue(lines[2].enabled);
                Assert.IsTrue(lines[3].enabled);
                // Thumb rendering depends on its own joints, not the index
                // data used by the separate thumb-state classifier.
                Assert.IsTrue(lines[4].enabled);
            });
        }

        public enum InvalidThumbCase { Missing, Coincident, NaN, Infinity }

        [TestCase(InvalidThumbCase.Missing)]
        [TestCase(InvalidThumbCase.Coincident)]
        [TestCase(InvalidThumbCase.NaN)]
        [TestCase(InvalidThumbCase.Infinity)]
        public void InvalidThumb_HidesOnlyThumb(InvalidThumbCase invalidCase)
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                var hand = FakeHandData.WithThumb(FakeHandData.Hand(5f), FakeHandData.Thumb(45f, 0f));
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");
                Assert.IsTrue(lines[4].enabled);

                switch (invalidCase)
                {
                    case InvalidThumbCase.Missing:
                        hand.thumb.isValid = false;
                        break;
                    case InvalidThumbCase.Coincident:
                        hand.thumb.distal = hand.thumb.proximal;
                        break;
                    case InvalidThumbCase.NaN:
                        hand.thumb.tip = new Vector3(float.NaN, 0f, 0f);
                        break;
                    case InvalidThumbCase.Infinity:
                        hand.thumb.tip = new Vector3(float.PositiveInfinity, 0f, 0f);
                        break;
                }

                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");
                Assert.IsFalse(lines[4].enabled);
                for (var i = 0; i < 4; ++i)
                    Assert.IsTrue(lines[i].enabled);
            });
        }

        static void WithVisualizer(System.Action<QuestHandAdapter, HandJointVisualizer,
            Transform, LineRenderer[]> check)
        {
            var owner = new GameObject("Visualizer test");
            var trackingObject = new GameObject("Camera Offset");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                trackingObject.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 45f, 0f));
                var adapter = owner.AddComponent<QuestHandAdapter>();
                var visualizer = owner.AddComponent<HandJointVisualizer>();
                SetField(visualizer, "m_Adapter", adapter);
                SetField(visualizer, "m_TrackingSpace", trackingObject.transform);
                SetField(visualizer, "m_Material", material);
                // Invoke directly: SendMessage asserts for normal behaviours in EditMode.
                Invoke(visualizer, "Start");
                var lines = (LineRenderer[])visualizer.GetType().GetField("m_Fingers",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(visualizer);
                check(adapter, visualizer, trackingObject.transform, lines);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(trackingObject);
                Object.DestroyImmediate(material);
            }
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
