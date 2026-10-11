using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPoseRecognition.Tests
{
    public class BaristaDemoTests
    {
        [TestCase(1, BaristaDrink.Coffee)] [TestCase(2, BaristaDrink.Coffee)] [TestCase(3, BaristaDrink.Coffee)]
        [TestCase(1, BaristaDrink.Tea)] [TestCase(2, BaristaDrink.Tea)] [TestCase(3, BaristaDrink.Tea)]
        public void EntireOrderRequiresAmountConfirmationDrinkPleaseAndFinalThankYou(int amount, BaristaDrink drink)
        {
            var order = new BaristaOrderSession();
            Assert.AreEqual(BaristaOrderStep.Amount, order.Step);
            Assert.IsTrue(order.ChooseAmount(amount));
            Assert.AreEqual(BaristaOrderStep.ConfirmAmount, order.Step);
            Assert.IsFalse(order.ChooseDrink(drink));
            Assert.IsTrue(order.Gesture(DirectGesture.Yes));
            Assert.IsTrue(order.ChooseDrink(drink));
            Assert.AreEqual(BaristaOrderStep.Please, order.Step);
            Assert.IsFalse(order.Gesture(DirectGesture.ThankYou));
            Assert.IsTrue(order.Gesture(DirectGesture.Please));
            StringAssert.Contains(amount.ToString(), order.Dialogue);
            StringAssert.Contains(drink == BaristaDrink.Coffee ? "coffee" : "tea", order.Dialogue);
            StringAssert.Contains("out shortly. Thank you!", order.Dialogue);
            Assert.IsFalse(order.Gesture(DirectGesture.ThankYou), "Do not skip the barista's response.");
            Assert.IsTrue(order.FinishResponse());
            Assert.IsTrue(order.Gesture(DirectGesture.ThankYou));
            Assert.AreEqual(BaristaOrderStep.Complete, order.Step);
            Assert.IsFalse(order.ChooseAmount(2));
            Assert.IsFalse(order.Gesture(DirectGesture.ThankYou));
        }

        [Test] public void InvalidOrOutOfOrderInputDoesNotAdvanceAndAmountCanBeCorrected()
        {
            var order = new BaristaOrderSession();
            Assert.IsFalse(order.ChooseAmount(0)); Assert.IsFalse(order.ChooseAmount(4));
            Assert.IsFalse(order.Gesture(DirectGesture.Yes)); Assert.IsFalse(order.FinishResponse());
            order.ChooseAmount(1); order.ChooseAmount(3);
            Assert.AreEqual(3, order.Amount);
            Assert.IsFalse(order.Gesture(DirectGesture.Please));
            order.Gesture(DirectGesture.Yes);
            Assert.IsFalse(order.ChooseAmount(2)); Assert.IsFalse(order.ChooseDrink(BaristaDrink.None));
            order.Restart();
            Assert.AreEqual(0, order.Amount); Assert.AreEqual(BaristaDrink.None, order.Drink);
            Assert.AreEqual(BaristaOrderStep.Amount, order.Step);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void AslNumbersUseThumbIndexAndMiddleRatherThanThreeNonThumbFingers(int number)
        {
            var hand = number == 1 ? FakeHandData.Hand(0, 90, 90, 90, Quaternion.identity, Vector3.zero)
                : FakeHandData.SplayedHand(0, 0, 90, 90, -18, 18, 0, 0);
            hand = FakeHandData.WithThumb(hand, FakeHandData.Thumb(number == 3 ? 90 : 0, 0));
            Assert.AreEqual(number, BaristaHandRules.Amount(hand, new HandPoseClassifierSettings()));
            hand.isTracked = false;
            Assert.AreEqual(0, BaristaHandRules.Amount(hand, new HandPoseClassifierSettings()));
        }

        [Test] public void UnknownThumbAndWrongThreeFingersCannotCountAsQuantity()
        {
            Assert.AreEqual(0, BaristaHandRules.Amount(FakeHandData.Hand(0, 0, 0, 90, Quaternion.identity, Vector3.zero), new HandPoseClassifierSettings()));
            Assert.AreEqual(0, BaristaHandRules.Amount(FakeHandData.Hand(0, 90, 90, 90, Quaternion.identity, Vector3.zero), new HandPoseClassifierSettings()));
        }

        [TestCase(1)] [TestCase(-1)]
        public void CoffeeNeedsTwoFistsAndACircularGrind(int direction)
        {
            var recognizer = new BaristaDrinkRecognizer(); bool detected = false;
            for (int i = 0; i <= 60; i++)
            {
                float angle = direction * i * Mathf.PI / 30;
                var result = recognizer.Update(i * .04, new Vector3(.05f * Mathf.Cos(angle), .1f, .05f * Mathf.Sin(angle)), true, false);
                detected |= result == BaristaDrink.Coffee;
            }
            Assert.IsTrue(detected);
        }

        [Test] public void TeaNeedsPinchAndCupWithDownThenUpMotion()
        {
            var pinch = FakeHandData.WithThumb(FakeHandData.Hand(45, 0, 0, 0, Quaternion.identity, Vector3.zero), FakeHandData.Thumb(60, 0));
            pinch.thumb.tip = pinch.index.tip;
            Assert.IsTrue(BaristaHandRules.TeaPinch(pinch, new HandPoseClassifierSettings()));
            var cup = FakeHandData.WithThumb(FakeHandData.Hand(30), FakeHandData.Thumb(60, 0));
            cup.thumb.tip = cup.index.tip + Vector3.right * .05f;
            Assert.IsTrue(BaristaHandRules.Cup(cup));
            var recognizer = new BaristaDrinkRecognizer(); bool detected = false;
            for (int i = 0; i <= 40; i++)
                detected |= recognizer.Update(i * .025, new Vector3(0, .15f - .08f * Mathf.Sin(i * Mathf.PI / 40), 0), false, true) == BaristaDrink.Tea;
            Assert.IsTrue(detected);
            pinch.middle.isValid = false;
            Assert.IsFalse(BaristaHandRules.TeaPinch(pinch, new HandPoseClassifierSettings()));
            cup.little.isValid = false; Assert.IsFalse(BaristaHandRules.Cup(cup));
        }

        [Test] public void StationaryHandsWrongShapesAndOneWayDipDoNotSelectDrink()
        {
            foreach (bool coffee in new[] { true, false })
            {
                var recognizer = new BaristaDrinkRecognizer();
                for (int i = 0; i < 60; i++) Assert.AreEqual(BaristaDrink.None, recognizer.Update(i * .025, Vector3.up * .12f, coffee, !coffee));
            }
            var empty = new BaristaDrinkRecognizer();
            for (int i = 0; i < 40; i++) Assert.AreEqual(BaristaDrink.None, empty.Update(i * .025, new Vector3(0, .18f - i * .003f, 0), false, true));
            Assert.AreEqual(BaristaDrink.None, empty.Update(1.1, Vector3.up * .15f, false, false));
            Assert.AreEqual(0, empty.SampleCount);
        }

        [Test] public void BaristaThankYouStartsBelowCamera()
        {
            var settings = new DirectGestureSettings { thankYouStartMaximumHeight = -.05f };
            foreach (float y in new[] { -.1f, .05f })
            {
                var recognizer = new DirectGestureRecognizer(); bool detected = false;
                for (int i = 0; i < 16; i++) detected |= recognizer.Update(new DirectGestureSample(i * .05,
                    new Vector3(0, y, .1f + i * .025f), 0, HandPose.Open), settings) == DirectGesture.ThankYou;
                Assert.AreEqual(y < 0, detected);
            }
        }

        [Test] public void SavedSceneHasBothTrackedHandsAnimatedHintsAndCompleteConversationWiring()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BaristaDemo.unity");
            try
            {
                BaristaDemoController demo = null; int visualizers = 0, hints = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (demo == null) demo = root.GetComponentInChildren<BaristaDemoController>(true);
                    visualizers += root.GetComponentsInChildren<HandJointVisualizer>(true).Length;
                    hints += root.GetComponentsInChildren<BaristaSignHint>(true).Length;
                }
                Assert.IsNotNull(demo); Assert.AreEqual(2, visualizers); Assert.AreEqual(8, hints);
                var serialized = new SerializedObject(demo);
                foreach (string field in new[] { "m_Right", "m_Left", "m_Head", "m_TrackingSpace", "m_Dialogue", "m_Hint", "m_Step", "m_Progress", "m_Gestures", "m_BaristaHead" })
                    Assert.IsNotNull(serialized.FindProperty(field).objectReferenceValue, field);
                Assert.AreEqual(HandSide.Right, ((QuestHandAdapter)serialized.FindProperty("m_Right").objectReferenceValue).HandSide);
                Assert.AreEqual(HandSide.Left, ((QuestHandAdapter)serialized.FindProperty("m_Left").objectReferenceValue).HandSide);
                Assert.AreEqual(5, serialized.FindProperty("m_HintGroups").arraySize);
                Assert.AreEqual(3, serialized.FindProperty("m_Cups").arraySize);
                var direct = new SerializedObject(serialized.FindProperty("m_Gestures").objectReferenceValue);
                Assert.AreEqual(-.05f, direct.FindProperty("m_MotionSettings.thankYouStartMaximumHeight").floatValue);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
