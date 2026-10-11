namespace HandPoseRecognition
{
    public enum BaristaOrderStep { Amount, ConfirmAmount, Drink, Please, BaristaResponse, ThankYou, Complete }
    public enum BaristaDrink { None, Coffee, Tea }

    /// <summary>The café conversation, independent of Unity and tracking.</summary>
    public sealed class BaristaOrderSession
    {
        public BaristaOrderStep Step { get; private set; }
        public int Amount { get; private set; }
        public BaristaDrink Drink { get; private set; }
        public string Order => Amount == 0 ? "No order yet" : Amount + " "
            + (Drink == BaristaDrink.None ? "drink" : Drink == BaristaDrink.Coffee ? "coffee" : "tea")
            + (Amount == 1 ? "" : "s");

        public void Restart() { Step = BaristaOrderStep.Amount; Amount = 0; Drink = BaristaDrink.None; }

        public bool ChooseAmount(int amount)
        {
            if ((Step != BaristaOrderStep.Amount && Step != BaristaOrderStep.ConfirmAmount) || amount < 1 || amount > 3) return false;
            Amount = amount; Step = BaristaOrderStep.ConfirmAmount; return true;
        }

        public bool ChooseDrink(BaristaDrink drink)
        {
            if (Step != BaristaOrderStep.Drink || (drink != BaristaDrink.Coffee && drink != BaristaDrink.Tea)) return false;
            Drink = drink; Step = BaristaOrderStep.Please; return true;
        }

        public bool Gesture(DirectGesture gesture)
        {
            if (Step == BaristaOrderStep.ConfirmAmount && gesture == DirectGesture.Yes) Step = BaristaOrderStep.Drink;
            else if (Step == BaristaOrderStep.Please && gesture == DirectGesture.Please) Step = BaristaOrderStep.BaristaResponse;
            else if (Step == BaristaOrderStep.ThankYou && gesture == DirectGesture.ThankYou) Step = BaristaOrderStep.Complete;
            else return false;
            return true;
        }

        public bool FinishResponse()
        {
            if (Step != BaristaOrderStep.BaristaResponse) return false;
            Step = BaristaOrderStep.ThankYou; return true;
        }

        public string Dialogue => Step == BaristaOrderStep.BaristaResponse || Step == BaristaOrderStep.ThankYou
            ? "I will get your " + Order + " out shortly. Thank you!"
            : Step == BaristaOrderStep.Complete ? "You're welcome! Enjoy your " + (Drink == BaristaDrink.Coffee ? "coffee" : "tea") + "."
            : Step == BaristaOrderStep.Drink ? "Great! Would you like coffee or tea?"
            : Step == BaristaOrderStep.Please ? "Got it. " + Order + "."
            : "Welcome in! What drink can I get you?";
    }
}
