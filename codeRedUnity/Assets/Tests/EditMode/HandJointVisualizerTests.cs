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
        public void TrackingLoss_HidesPreviouslyVisibleFingers()
        {
            WithVisualizer((adapter, visualizer, trackingSpace, lines) =>
            {
                SetField(adapter, "m_CurrentHand", FakeHandData.Hand(5f));
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
                var hand = FakeHandData.Hand(5f);
                hand.index.isValid = false;
                SetField(adapter, "m_CurrentHand", hand);
                Invoke(visualizer, "LateUpdate");
                Assert.IsFalse(lines[0].enabled);
                Assert.IsTrue(lines[1].enabled);
                Assert.IsTrue(lines[2].enabled);
                Assert.IsTrue(lines[3].enabled);
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
