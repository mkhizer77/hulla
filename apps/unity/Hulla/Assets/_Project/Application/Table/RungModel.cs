using System;
using System.Collections.Generic;
using Hulla.Protocol;

namespace Hulla.Application.Table
{
    /// <summary>
    /// What this client knows about a Rung deal (Rung design 2), built only from server messages. The
    /// table model owns the seats, the host and the room; this holds the deal: the call, the rung, the
    /// trick, the pile and the two teams' counts. Seats 0 and 2 are team 0, seats 1 and 3 team 1 (RG1).
    /// </summary>
    public sealed class RungModel
    {
        public const int Seats = 4;

        private readonly List<string> _hand = new List<string>();
        private readonly List<TrickPlay> _trick = new List<TrickPlay>();
        private readonly List<TrickPlay> _lastTrick = new List<TrickPlay>();
        private readonly HashSet<string> _legal = new HashSet<string>();
        private readonly List<char> _suits = new List<char>();
        private long[] _handCounts = new long[Seats];
        private Comparison<string> _sort = CardOrder.Compare;

        public IReadOnlyList<string> Hand => _hand;

        public IReadOnlyList<TrickPlay> Trick => _trick;

        /// <summary>The trick that was won last, kept so the view can show how it ended.</summary>
        public IReadOnlyList<TrickPlay> LastTrick => _lastTrick;

        public RungPhase Phase { get; private set; } = RungPhase.Ended;

        public string RoundId { get; private set; } = "";

        public long DealerSeat { get; private set; }

        /// <summary>The seat after the dealer: it names the rung and leads (RG4).</summary>
        public long CallerSeat { get; private set; }

        /// <summary>The rung as a suit letter (S, H, C, D), or null before the call.</summary>
        public char? Trump { get; private set; }

        public long? TurnSeat { get; private set; }

        public long DeadlineTs { get; private set; }

        /// <summary>Tricks per team.</summary>
        public long[] Counts { get; private set; } = new long[2];

        /// <summary>Tricks lying in the centre that nobody owns yet (Double Sar, RG16).</summary>
        public long Pile { get; private set; }

        /// <summary>The seat whose run is alive: its next trick in a row takes the pile (RG17). Null in Single Sar.</summary>
        public long? RunSeat { get; private set; }

        /// <summary>The seat that won the last trick, until the next card is led.</summary>
        public long? LastWinner { get; private set; }

        /// <summary>The seat and trick count of the pile taken last, for the view's sweep; cleared on the next lead.</summary>
        public RungPileTaken? LastPile { get; private set; }

        /// <summary>The team playing on for the bigger prize (RG26), or null.</summary>
        public long? Chasing { get; private set; }

        public long Redeals { get; private set; }

        public RungRules? Rules { get; private set; }

        public string Commitment { get; private set; } = "";

        /// <summary>The suits the caller may name; filled by my turn_start in the call.</summary>
        public IReadOnlyList<char> CallableSuits => _suits;

        /// <summary>The court choice is mine and the server has asked for it (RG26).</summary>
        public bool ChoiceOffered { get; private set; }

        /// <summary>Whether I may ask for a redeal now (RG7).</summary>
        public bool CanRedeal { get; private set; }

        public string? PendingCard { get; private set; }

        public RungRoundEnd? LastResult { get; private set; }

        /// <summary>The bazi score by team, from the series; null before the first deal ends.</summary>
        public RungTeams? Teams { get; private set; }

        /// <summary>How many deals of the bazi are done.</summary>
        public long DealsPlayed { get; private set; }

        public bool BaziFinished { get; private set; }

        public bool DoubleSar => Rules?.Collect != Collect.Single;

        public long HandCount(int seat) => seat >= 0 && seat < _handCounts.Length ? _handCounts[seat] : 0;

        public static int TeamOf(long seat) => (int)(seat % 2);

        public static long PartnerOf(long seat) => (seat + 2) % Seats;

        public bool IsLegal(string card) => _legal.Contains(card) && PendingCard == null;

        public int LegalCount => _legal.Count;

        /// <summary>The suit the open trick asks for, or null when nobody has led.</summary>
        public char? LedSuit => _trick.Count > 0 ? _trick[0].Card[1] : (char?)null;

        /// <summary>A cosmetic lift until the server answers.</summary>
        public void MarkPending(string card) => PendingCard = card;

        public void SetSort(Comparison<string> sort)
        {
            _sort = sort;
            SortHand();
        }

        /// <summary>The whole deal as the server sees it for this seat (table_state, snapshot).</summary>
        public void ApplyView(RungView view, long? mySeat)
        {
            Phase = view.Phase;
            DealerSeat = view.DealerSeat;
            CallerSeat = view.CallerSeat;
            Trump = SuitChar(view.Trump);
            TurnSeat = view.TurnSeat;
            Counts = Pair(view.Counts);
            Pile = view.Pile;
            RunSeat = view.Pile > 0 ? view.LastWinner : null;
            LastWinner = null;
            LastPile = null;
            Chasing = view.Chasing;
            Redeals = view.Redeals;
            Rules = view.Rules;
            Commitment = view.Commitment ?? "";
            _handCounts = view.HandCounts.ToArray();
            _hand.Clear();
            if (view.Hand != null)
            {
                _hand.AddRange(view.Hand);
            }

            SortHand();
            _trick.Clear();
            _lastTrick.Clear();
            foreach (var play in view.Trick.Plays)
            {
                _trick.Add(new TrickPlay(play.Seat, play.Card));
            }

            PendingCard = null;
            if (TurnSeat != mySeat)
            {
                ClearTurn();
            }
        }

        /// <summary>The room is between deals: nothing on the table, the last result kept for the screen.</summary>
        public void ClearDeal()
        {
            Phase = RungPhase.Ended;
            TurnSeat = null;
            _trick.Clear();
            PendingCard = null;
            ClearTurn();
        }

        /// <summary>The room left Rung or started a new bazi: nothing of the old one is shown.</summary>
        public void Reset()
        {
            ClearDeal();
            _hand.Clear();
            _lastTrick.Clear();
            LastResult = null;
            Teams = null;
            DealsPlayed = 0;
            BaziFinished = false;
            Trump = null;
            Counts = new long[2];
            Pile = 0;
            RunSeat = null;
            LastWinner = null;
            LastPile = null;
            Chasing = null;
        }

        public void ApplySeries(SeriesState? series)
        {
            if (series == null)
            {
                return;
            }

            Teams = series.Teams;
            DealsPlayed = series.RoundsPlayed;
            BaziFinished = series.Finished;
        }

        /// <summary>Applies a Rung message; false when the payload is not one.</summary>
        public bool Apply(object payload, long? mySeat)
        {
            switch (payload)
            {
                case RungRoundStart start:
                    RoundId = start.RoundId;
                    Phase = RungPhase.Call;
                    DealerSeat = start.DealerSeat;
                    CallerSeat = start.CallerSeat;
                    TurnSeat = start.CallerSeat;
                    Trump = null;
                    Counts = new long[2];
                    Pile = 0;
                    RunSeat = null;
                    LastWinner = null;
                    LastPile = null;
                    Chasing = null;
                    Redeals = 0;
                    Commitment = start.Commitment ?? "";
                    _handCounts = start.HandCounts.ToArray();
                    _trick.Clear();
                    _lastTrick.Clear();
                    LastResult = null;
                    PendingCard = null;
                    if (TurnSeat != mySeat)
                    {
                        ClearTurn();
                    }

                    return true;
                case RungHand hand:
                    _hand.Clear();
                    _hand.AddRange(hand.Cards);
                    SortHand();
                    return true;
                case RungRedealt redealt:
                    Redeals = redealt.Redeals;
                    Commitment = redealt.Commitment ?? "";
                    _handCounts = redealt.HandCounts.ToArray();
                    TurnSeat = redealt.Seat;
                    ClearTurn();
                    return true;
                case RungTrumpCalled called:
                    Trump = SuitChar(called.Suit);
                    ClearTurn();
                    return true;
                case RungHandsCompleted completed:
                    _handCounts = completed.HandCounts.ToArray();
                    Phase = RungPhase.Play;
                    TurnSeat = CallerSeat;
                    return true;
                case RungCardPlayed played:
                    ApplyCard(played, mySeat);
                    return true;
                case RungTrickWon won:
                    _lastTrick.Clear();
                    _lastTrick.AddRange(_trick);
                    _trick.Clear();
                    Counts = Pair(won.Counts);
                    Pile = won.Pile;
                    LastWinner = won.Winner;
                    RunSeat = won.Pile > 0 ? won.Winner : (long?)null;
                    TurnSeat = won.Winner;
                    return true;
                case RungPileTaken taken:
                    Counts = Pair(taken.Counts);
                    Pile = 0;
                    RunSeat = null;
                    LastPile = taken;
                    return true;
                case RungCourtChoiceOpened opened:
                    Phase = RungPhase.CourtChoice;
                    TurnSeat = opened.Seat;
                    return true;
                case RungCourtChoiceMade made:
                    Phase = RungPhase.Play;
                    Chasing = made.PlayOn ? TeamOf(made.Seat) : (long?)null;
                    TurnSeat = made.Seat;
                    ClearTurn();
                    return true;
                case RungRoundEnd end:
                    LastResult = end;
                    Counts = Pair(end.Counts);
                    Phase = RungPhase.Ended;
                    TurnSeat = null;
                    PendingCard = null;
                    ClearTurn();
                    ApplySeries(end.Series);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>My turn: the legal cards in play, the suits and the redeal in the call (RG6, RG7).</summary>
        public void ApplyTurnStart(TurnStart turn)
        {
            RoundId = turn.RoundId;
            TurnSeat = turn.Seat;
            DeadlineTs = turn.DeadlineTs;
            ClearTurn();
            foreach (var card in turn.Legal)
            {
                _legal.Add(card);
            }

            if (turn.Suits != null)
            {
                foreach (var suit in turn.Suits)
                {
                    _suits.Add(SuitChar(suit) ?? 'S');
                }
            }

            CanRedeal = turn.CanRedeal == true;
            ChoiceOffered = turn.Phase == "courtChoice";
            switch (turn.Phase)
            {
                case "call":
                    Phase = RungPhase.Call;
                    break;
                case "courtChoice":
                    Phase = RungPhase.CourtChoice;
                    break;
                case "play":
                    Phase = RungPhase.Play;
                    break;
            }
        }

        public void ClearPending() => PendingCard = null;

        private void ApplyCard(RungCardPlayed played, long? mySeat)
        {
            var seat = (int)played.Seat;
            if (played.Seat == mySeat)
            {
                _hand.Remove(played.Card);
                PendingCard = null;
                ClearTurn();
            }

            if (seat < _handCounts.Length && _handCounts[seat] > 0)
            {
                _handCounts[seat]--;
            }

            if (_trick.Count == 0)
            {
                _lastTrick.Clear();
                LastWinner = null;
                LastPile = null;
            }

            _trick.Add(new TrickPlay(played.Seat, played.Card));
            // RG3: the engine counts seats up; the fourth card ends the trick and trick_won names the leader.
            TurnSeat = _trick.Count < Seats ? (played.Seat + 1) % Seats : (long?)null;
        }

        private void ClearTurn()
        {
            _legal.Clear();
            _suits.Clear();
            CanRedeal = false;
            ChoiceOffered = false;
        }

        private void SortHand() => _hand.Sort(_sort);

        private static long[] Pair(IReadOnlyList<long>? values) =>
            values != null && values.Count >= 2 ? new[] { values[0], values[1] } : new long[2];

        private static char? SuitChar(Suit? suit)
        {
            switch (suit)
            {
                case Suit.S:
                    return 'S';
                case Suit.H:
                    return 'H';
                case Suit.C:
                    return 'C';
                case Suit.D:
                    return 'D';
                default:
                    return null;
            }
        }
    }
}
