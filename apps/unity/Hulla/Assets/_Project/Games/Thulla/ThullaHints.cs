using Hulla.Application.Settings;
using Hulla.Application.Table;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// Contextual hints for a player's first five live rounds (GDD 17.4): each one shows once, ever,
    /// at the moment it explains, and the choice is saved so it never repeats.
    /// </summary>
    public sealed class ThullaHints
    {
        public const int FirstRounds = 5;
        private const string RoundsKey = "hulla.ftue.rounds";
        private const string HintPrefix = "hulla.ftue.hint.";
        private readonly ISettingsStore _store;

        public ThullaHints(ISettingsStore store)
        {
            _store = store;
        }

        public bool Active => _store.GetInt(RoundsKey, 0) < FirstRounds;

        /// <summary>The hint for this moment, if one is due and hasn't been shown; showing it marks it seen.</summary>
        public string? Due(TableModel model, bool thullaJustPlayed)
        {
            if (!Active || !model.InRound || model.MySeat == null)
            {
                return null;
            }

            if (model.MyTurn && model.CanTakeHand && Take("take-hand"))
            {
                return TutorialText.HintTakeHand;
            }

            if (model.MyTurn && HasDimmed(model) && Take("legal"))
            {
                return TutorialText.HintLegal;
            }

            if (thullaJustPlayed && Take("quick-chat"))
            {
                return TutorialText.HintQuickChat;
            }

            if (model.Trick.Count > 0 && model.LeaderSeat != model.MySeat && Take("leader"))
            {
                return TutorialText.HintLeader;
            }

            return null;
        }

        /// <summary>True the first time ever a player asks for a hand, so the ask can be explained (GDD 17.4).</summary>
        public bool ExplainAsk() => Take("ask-hand");

        public void RoundEnded()
        {
            _store.SetInt(RoundsKey, _store.GetInt(RoundsKey, 0) + 1);
            _store.Save();
        }

        private static bool HasDimmed(TableModel model)
        {
            foreach (var card in model.Hand)
            {
                if (!model.IsLegal(card))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Take(string id)
        {
            if (_store.GetInt(HintPrefix + id, 0) == 1)
            {
                return false;
            }

            _store.SetInt(HintPrefix + id, 1);
            _store.Save();
            return true;
        }
    }
}
