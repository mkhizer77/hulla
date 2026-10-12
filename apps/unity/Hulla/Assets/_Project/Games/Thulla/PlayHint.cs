using Hulla.Application.Table;

namespace Hulla.Games.Thulla
{
    /// <summary>The one-line reason a tapped card can't be played (GDD 14.4), from what the client knows.</summary>
    public static class PlayHint
    {
        public enum Reason
        {
            None,
            NotYourTurn,
            MustFollow,
            LeadAceOfSpades,
            Waiting,
        }

        public static Reason For(TableModel model, string card)
        {
            if (!model.InRound || model.MySeat == null)
            {
                return Reason.Waiting;
            }

            if (!model.MyTurn)
            {
                return model.PendingCard != null ? Reason.Waiting : Reason.NotYourTurn;
            }

            if (model.IsLegal(card))
            {
                return Reason.None;
            }

            if (model.Trick.Count == 0 && model.IsLegal("AS") && card != "AS")
            {
                return Reason.LeadAceOfSpades;
            }

            var led = model.LedSuit;
            return led != null && card[1] != led.Value && model.HoldsSuit(led.Value) ? Reason.MustFollow : Reason.Waiting;
        }
    }
}
