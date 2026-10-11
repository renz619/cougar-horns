using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    /// <summary>
    /// Integration tests for QuestHandAdapter.ApplyTrackingSpace with a real
    /// XR Origin / Camera Offset transform hierarchy. They catch a missing or
    /// doubled rotation, and check that a missing Tracking Space explicitly
    /// clears orientation.
    /// </summary>
    public class ThumbsUpIntegrationTests
    {
        const float k_Curled = 80f;

        readonly List<GameObject> m_Created = new List<GameObject>();
        HandPoseClassifierSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new HandPoseClassifierSettings();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var created in m_Created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            m_Created.Clear();
        }

        // XR Origin at the root, Camera Offset under it, like the demo scene.
        Transform CreateRig(Quaternion originRotation, Quaternion offsetLocalRotation,
            Vector3 originPosition = default, float originScale = 1f)
        {
            var origin = new GameObject("XR Origin");
            m_Created.Add(origin);
            origin.transform.SetPositionAndRotation(originPosition, originRotation);
            origin.transform.localScale = Vector3.one * originScale;

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(origin.transform, false);
            offset.transform.localPosition = new Vector3(0f, 1.1176f, 0f);
            offset.transform.localRotation = offsetLocalRotation;
            return offset.transform;
        }

        // Fist with the thumb out (Extended), with the whole hand rotated in
        // tracking space so the thumb direction (proximal to tip) is local +X.
        static HandJointData FistWithThumbAlongLocalX()
        {
            var hand = FakeHandData.WithThumb(FakeHandData.Hand(k_Curled), FakeHandData.Thumb(45f, 0f));
            var toLocalX = Quaternion.FromToRotation(hand.thumb.tip - hand.thumb.proximal, Vector3.right);
            return FakeHandData.Transformed(hand, toLocalX, Vector3.zero, 1f);
        }

        // 90 degrees about Z turns local +X into world +Y.
        static readonly Quaternion k_XToUp = Quaternion.Euler(0f, 0f, 90f);

        HandPose Pose(in HandJointData hand) => HandPoseClassifier.ClassifyPoseWithOrientation(hand, m_Settings);

        static float Angle(in HandJointData hand) => HandPoseClassifier.ThumbAngleFromWorldUpDegrees(hand);

        // --- Applied exactly once -------------------------------------------------

        [Test]
        public void RotatedOrigin_AppliedOnce_ThumbPointsUp()
        {
            var offset = CreateRig(k_XToUp, Quaternion.identity);
            var hand = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), offset);

            Assert.IsTrue(hand.hasTrackingSpaceToWorld);
            Assert.Less(Quaternion.Angle(offset.rotation, hand.trackingSpaceToWorld), 1e-3f);
            Assert.AreEqual(0f, Angle(hand), 0.05f);
            Assert.AreEqual(HandPose.ThumbsUp, Pose(hand));
        }

        [Test]
        public void MissingRotation_ThumbSideways_IsFist()
        {
            // What the classifier would see if the rig rotation were ignored.
            var missing = FakeHandData.WithTrackingSpace(FistWithThumbAlongLocalX(), Quaternion.identity);

            Assert.AreEqual(90f, Angle(missing), 0.05f);
            Assert.AreEqual(HandPose.Fist, Pose(missing));
        }

        [Test]
        public void DoubledRotation_ThumbSideways_IsFist()
        {
            // What the classifier would see if the rotation were applied twice
            // (e.g. Camera Offset's world rotation times the XR Origin's again).
            var offset = CreateRig(k_XToUp, Quaternion.identity);
            var doubled = FakeHandData.WithTrackingSpace(FistWithThumbAlongLocalX(), offset.rotation * offset.parent.rotation);

            Assert.AreEqual(90f, Angle(doubled), 0.05f);
            Assert.AreEqual(HandPose.Fist, Pose(doubled));
        }

        [Test]
        public void RotatedCameraOffsetUnderRotatedOrigin_CopiesComposedWorldRotation()
        {
            var originRotation = Quaternion.Euler(0f, 30f, 0f);
            var offsetLocal = Quaternion.Euler(20f, 0f, 0f);
            var offset = CreateRig(originRotation, offsetLocal);

            var hand = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), offset);

            Assert.Less(Quaternion.Angle(originRotation * offsetLocal, hand.trackingSpaceToWorld), 1e-3f);
            Assert.Greater(Quaternion.Angle(originRotation, hand.trackingSpaceToWorld), 1f);
        }

        [Test]
        public void RigPositionAndPositiveUniformScale_DoNotChangeResult()
        {
            var plain = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), CreateRig(k_XToUp, Quaternion.identity));
            var movedScaled = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(),
                CreateRig(k_XToUp, Quaternion.identity, new Vector3(4f, -2f, 7f), 2f));

            Assert.AreEqual(Angle(plain), Angle(movedScaled), 1e-3f);
            Assert.AreEqual(HandPose.ThumbsUp, Pose(movedScaled));
        }

        [Test]
        public void ApplyTrackingSpace_LeavesJointPositionsInTrackingSpace()
        {
            var before = FistWithThumbAlongLocalX();
            var after = QuestHandAdapter.ApplyTrackingSpace(before, CreateRig(k_XToUp, Quaternion.identity, new Vector3(1f, 2f, 3f)));

            Assert.AreEqual(before.thumb.tip, after.thumb.tip);
            Assert.AreEqual(before.thumb.proximal, after.thumb.proximal);
            Assert.AreEqual(before.index.tip, after.index.tip);
            Assert.AreEqual(before.little.proximal, after.little.proximal);
            Assert.AreEqual(before.isTracked, after.isTracked);
        }

        // --- Missing Tracking Space ------------------------------------------------

        [Test]
        public void NullTrackingSpace_ClearsOrientation_IsFist()
        {
            var hand = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), null);

            Assert.IsFalse(hand.hasTrackingSpaceToWorld);
            Assert.AreEqual(default(Quaternion), hand.trackingSpaceToWorld);
            Assert.IsNaN(Angle(hand));
            Assert.AreEqual(HandPose.Fist, Pose(hand));
        }

        [Test]
        public void ValidThenMissing_TransitionClearsOrientation()
        {
            var offset = CreateRig(k_XToUp, Quaternion.identity);
            var valid = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), offset);
            Assert.AreEqual(HandPose.ThumbsUp, Pose(valid));

            // The next update arrives with no Tracking Space: the old rotation
            // carried in the snapshot must not survive.
            var missing = QuestHandAdapter.ApplyTrackingSpace(valid, null);

            Assert.IsFalse(missing.hasTrackingSpaceToWorld);
            Assert.AreEqual(default(Quaternion), missing.trackingSpaceToWorld);
            Assert.AreEqual(ThumbDirectionState.Unknown, HandPoseClassifier.ClassifyThumbDirection(missing, m_Settings));
            Assert.AreEqual(HandPose.Fist, Pose(missing));
        }

        [Test]
        public void DestroyedTrackingSpace_TreatedAsMissing()
        {
            var offset = CreateRig(k_XToUp, Quaternion.identity);
            var valid = QuestHandAdapter.ApplyTrackingSpace(FistWithThumbAlongLocalX(), offset);

            Object.DestroyImmediate(offset.gameObject);
            var afterDestroy = QuestHandAdapter.ApplyTrackingSpace(valid, offset);

            Assert.IsFalse(afterDestroy.hasTrackingSpaceToWorld);
            Assert.AreEqual(HandPose.Fist, Pose(afterDestroy));
        }

        // --- Adapter component ----------------------------------------------------

        [Test]
        public void AdapterWithoutUpdates_ReportsNoOrientation()
        {
            // The adapter's snapshot starts empty; a Tracking Space alone never
            // makes orientation available without hand data.
            var owner = new GameObject("Adapter");
            m_Created.Add(owner);
            var adapter = owner.AddComponent<QuestHandAdapter>();

            Assert.IsFalse(adapter.currentHand.hasTrackingSpaceToWorld);
            Assert.AreEqual(HandPose.Unknown, Pose(adapter.currentHand));
        }
    }
}
