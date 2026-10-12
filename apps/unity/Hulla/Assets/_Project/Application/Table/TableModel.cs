using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Protocol;

namespace Hulla.Application.Table
{
    /// <summary>
    /// What this client knows about one table, built only from server messages. The server is the
    /// authority: the hand comes from table_state and is kept current by the events, the legal mask
    /// comes from turn_start, and anything the events cannot reconstruct asks for a snapshot.
    /// </summary>
    public sealed class TableModel
    {
        private readonly List<SeatInfo> _seats = new List<SeatInfo>();
        private readonly List<string> _hand = new List<string>();
        private readonly List<TrickPlay> _trick = new List<TrickPlay>();
        private readonly List<TrickPlay> _lastTrick = new List<TrickPlay>();
        private readonly HashSet<string> _legal = new HashSet<string>();
        private readonly List<long> _escapeOrder = new List<long>();
        private long[] _handCounts = new long[0];
        private bool[] _escaped = new bool[0];

        public TableModel(string userId)
        {
            UserId = userId;
        }

        /// <summary>Raised after every applied message so views can refresh.</summary>
        public event Action? Changed;

        /// <summary>Raised for each quick-chat phrase or emote, so views can show it once.</summary>
        public event Action<ChatLine>? Chat;

        public string UserId { get; }

        public string Code { get; private set; } = "";

        public TableStatus Status { get; private set; } = TableStatus.Open;

        public bool Closed { get; private set; }

        public TableMode Mode { get; private set; } = TableMode.Private;

        /// <summary>The preset, overrides and table settings the table was opened with.</summary>
        public RulesSelection? Selection { get; private set; }

        /// <summary>The toggles the engine resolved for the current round; null before the first deal.</summary>
        public ThullaRules? ActiveRules { get; private set; }

        public bool IsPrivate => Mode == TableMode.Private;

        /// <summary>When a public table deals itself (GDD 9.5); null when no countdown is running.</summary>
        public long? AutoStartAtTs { get; private set; }

        public long? HostSeat { get; private set; }

        public long? MySeat { get; private set; }

        public bool IsHost => MySeat != null && MySeat == HostSeat;

        public IReadOnlyList<SeatInfo> Seats => _seats;

        public bool InRound { get; private set; }

        public string RoundId { get; private set; } = "";

        public int SeatCount => _handCounts.Length;

        public IReadOnlyList<string> Hand => _hand;

        public IReadOnlyList<TrickPlay> Trick => _trick;

        /// <summary>The cards of the trick that resolved last, kept so the view can show how it ended.</summary>
        public IReadOnlyList<TrickPlay> LastTrick => _lastTrick;

        public long? LastPicker { get; private set; }

        /// <summary>Cards out of play this round (resolved tricks nobody picked up).</summary>
        public long DiscardCount { get; private set; }

        /// <summary>True once the round's fairness commitment arrived (R6); the seed is revealed at round end.</summary>
        public bool FairDealCommitted { get; private set; }

        /// <summary>Who leads the open trick, or leads next when it is empty.</summary>
        public long? LeaderSeat => _trick.Count > 0 ? _trick[0].Seat : TurnSeat;

        /// <summary>The suit the open trick asks for, or null when nobody has led yet.</summary>
        public char? LedSuit => _trick.Count > 0 ? _trick[0].Card[1] : (char?)null;

        public bool HoldsSuit(char suit)
        {
            foreach (var card in _hand)
            {
                if (card[1] == suit)
                {
                    return true;
                }
            }

            return false;
        }

        public long? TurnSeat { get; private set; }

        public bool MyTurn => MySeat != null && TurnSeat == MySeat && _legal.Count > 0;

        public long DeadlineTs { get; private set; }

        public bool CanTakeHand { get; private set; }

        public IReadOnlyList<long> EscapeOrder => _escapeOrder;

        public RoundEnd? LastResult { get; private set; }

        public string? LastErrorCode { get; private set; }

        public string? PendingCard { get; private set; }

        public long HandCount(int seat) => seat < _handCounts.Length ? _handCounts[seat] : 0;

        public bool IsEscaped(int seat) => seat < _escaped.Length && _escaped[seat];

        public bool IsLegal(string card) => _legal.Contains(card) && PendingCard == null;

        /// <summary>A cosmetic lift: the card leaves the hand view until move_applied or move_rejected.</summary>
        public void MarkPending(string card)
        {
            PendingCard = card;
            Raise();
        }

        public void Apply(ServerMessage message)
        {
            switch (message.Payload)
            {
                case TableState state:
                    ApplyTableState(state);
                    break;
                case Snapshot snapshot:
                    ApplyTableState(snapshot.State);
                    break;
                case RoundStart start:
                    ApplyRoundStart(start);
                    break;
                case TurnStart turn:
                    ApplyTurnStart(turn);
                    break;
                case MoveApplied applied:
                    ApplyMoveApplied(applied);
                    break;
                case MoveRejected _:
                    PendingCard = null;
                    break;
                case TrickResolved resolved:
                    ApplyTrickResolved(resolved);
                    break;
                case Pickup pickup:
                    if (pickup.Seat == MySeat)
                    {
                        _hand.AddRange(pickup.Cards);
                        SortHand();
                    }

                    break;
                case Escape escape:
                    MarkEscaped(escape.Seat);
                    break;
                case PrivateDraw draw:
                    ApplyPrivateDraw(draw);
                    break;
                case HandTaken taken:
                    ApplyHandTaken(taken);
                    break;
                case RoundEnd end:
                    LastResult = end;
                    InRound = false;
                    TurnSeat = null;
                    _legal.Clear();
                    PendingCard = null;
                    break;
                case Presence presence:
                    ApplyPresence(presence);
                    break;
                case HostChanged changed:
                    HostSeat = changed.Seat;
                    break;
                case TableClosed _:
                    Closed = true;
                    Status = TableStatus.Closed;
                    break;
                case Error error:
                    LastErrorCode = error.Code;
                    break;
                case QuickChatSent said:
                    Chat?.Invoke(new ChatLine(said.Seat, said.PhraseId, null, null));
                    break;
                case EmoteSent emote:
                    Chat?.Invoke(new ChatLine(emote.Seat, null, emote.EmoteId, emote.TargetSeat));
                    break;
            }

            Raise();
        }

        /// <summary>True when the events cannot rebuild this client's hand and a snapshot is needed.</summary>
        public bool NeedsSnapshotAfter(object payload) =>
            payload is HandTaken taken && taken.Taker == MySeat;

        private void ApplyTableState(TableState state)
        {
            Code = state.Code;
            Status = state.Status;
            Mode = state.Mode;
            Selection = state.Rules;
            AutoStartAtTs = state.AutoStart?.StartsAtTs;
            HostSeat = state.HostSeat;
            _seats.Clear();
            MySeat = null;
            foreach (var seat in state.Seats)
            {
                _seats.Add(new SeatInfo(seat.SeatSeat, seat.PlayerId, seat.DisplayName, seat.Ready, seat.Connected, seat.AutoPlay));
                if (seat.PlayerId == UserId)
                {
                    MySeat = seat.SeatSeat;
                }
            }

            var round = state.Round;
            PendingCard = null;
            if (round == null)
            {
                InRound = false;
                LastResult = null;
                TurnSeat = null;
                _legal.Clear();
                _trick.Clear();
                return;
            }

            InRound = round.Phase != Phase.Ended && state.Status == TableStatus.InRound;
            ActiveRules = round.Rules;
            DiscardCount = round.DiscardCount;
            FairDealCommitted = !string.IsNullOrEmpty(round.Commitment);
            var count = (int)round.SeatCount;
            _handCounts = new long[count];
            _escaped = new bool[count];
            _hand.Clear();
            for (var i = 0; i < count && i < round.Seats.Count; i++)
            {
                var seat = round.Seats[i];
                _handCounts[i] = seat.HandCount;
                _escaped[i] = seat.Escaped;
                if (i == MySeat && seat.Hand != null)
                {
                    _hand.AddRange(seat.Hand);
                }
            }

            SortHand();
            _escapeOrder.Clear();
            _escapeOrder.AddRange(round.EscapeOrder);
            _trick.Clear();
            foreach (var play in round.Trick.Plays)
            {
                _trick.Add(new TrickPlay(play.Seat, play.Card));
            }

            TurnSeat = round.TurnSeat;
            if (TurnSeat != MySeat)
            {
                _legal.Clear();
            }
        }

        private void ApplyRoundStart(RoundStart start)
        {
            RoundId = start.RoundId;
            InRound = true;
            AutoStartAtTs = null;
            Status = TableStatus.InRound;
            LastResult = null;
            _handCounts = start.HandCounts.ToArray();
            _escaped = new bool[_handCounts.Length];
            _escapeOrder.Clear();
            _trick.Clear();
            _lastTrick.Clear();
            LastPicker = null;
            DiscardCount = 0;
            FairDealCommitted = !string.IsNullOrEmpty(start.Commitment);
            TurnSeat = start.LeaderSeat;
        }

        private void ApplyTurnStart(TurnStart turn)
        {
            if (turn.Seat != MySeat)
            {
                return;
            }

            RoundId = turn.RoundId;
            TurnSeat = turn.Seat;
            DeadlineTs = turn.DeadlineTs;
            CanTakeHand = turn.CanTakeHand;
            _legal.Clear();
            foreach (var card in turn.Legal)
            {
                _legal.Add(card);
            }
        }

        private void ApplyMoveApplied(MoveApplied applied)
        {
            var seat = (int)applied.Seat;
            if (applied.Seat == MySeat)
            {
                _legal.Clear();
                CanTakeHand = false;
                PendingCard = null;
            }

            if (applied.Move.Type != TypeEnum.PlayCard || applied.Move.Card == null)
            {
                return;
            }

            if (applied.Seat == MySeat)
            {
                _hand.Remove(applied.Move.Card);
            }

            if (seat < _handCounts.Length && _handCounts[seat] > 0)
            {
                _handCounts[seat]--;
            }

            if (_trick.Count == 0)
            {
                _lastTrick.Clear();
                LastPicker = null;
            }

            _trick.Add(new TrickPlay(applied.Seat, applied.Move.Card));
            TurnSeat = NextActiveSeat(seat);
        }

        private void ApplyTrickResolved(TrickResolved resolved)
        {
            _lastTrick.Clear();
            _lastTrick.AddRange(_trick);
            _trick.Clear();
            LastPicker = resolved.Picker;
            if (resolved.Picker == null)
            {
                DiscardCount += resolved.Cards.Count;
            }

            _handCounts = resolved.HandCounts.ToArray();
            if (_escaped.Length != _handCounts.Length)
            {
                Array.Resize(ref _escaped, _handCounts.Length);
            }

            foreach (var escape in resolved.Escapes)
            {
                MarkEscaped(escape.Seat);
            }

            TurnSeat = resolved.NextLeader;
        }

        private void ApplyPrivateDraw(PrivateDraw draw)
        {
            if (draw.Seat == MySeat)
            {
                _hand.Add(draw.Card);
                SortHand();
            }
        }

        private void ApplyHandTaken(HandTaken taken)
        {
            var taker = (int)taken.Taker;
            var target = (int)taken.Target;
            if (taker < _handCounts.Length)
            {
                _handCounts[taker] += taken.CardCount;
            }

            if (target < _handCounts.Length)
            {
                _handCounts[target] = 0;
            }

            if (taken.Target == MySeat)
            {
                _hand.Clear();
            }
        }

        private void ApplyPresence(Presence presence)
        {
            if (presence.Seat == null)
            {
                return;
            }

            var index = (int)presence.Seat.Value;
            if (index >= _seats.Count)
            {
                return;
            }

            var old = _seats[index];
            switch (presence.Status)
            {
                case Hulla.Protocol.Status.Joined:
                case Hulla.Protocol.Status.Reconnected:
                    _seats[index] = new SeatInfo(old.Seat, presence.PlayerId, presence.DisplayName ?? old.DisplayName, old.Ready, true, false);
                    if (presence.PlayerId == UserId)
                    {
                        MySeat = old.Seat;
                    }

                    break;
                case Hulla.Protocol.Status.Disconnected:
                    _seats[index] = new SeatInfo(old.Seat, old.PlayerId, old.DisplayName, old.Ready, false, true);
                    break;
                case Hulla.Protocol.Status.AutoPlay:
                    _seats[index] = new SeatInfo(old.Seat, old.PlayerId, old.DisplayName, old.Ready, old.Connected, true);
                    break;
                case Hulla.Protocol.Status.Left:
                case Hulla.Protocol.Status.Kicked:
                case Hulla.Protocol.Status.Spectating:
                    _seats[index] = new SeatInfo(old.Seat, null, null, false, false, false);
                    if (presence.PlayerId == UserId)
                    {
                        MySeat = null;
                    }

                    break;
            }
        }

        private void MarkEscaped(long seat)
        {
            if (seat < _escaped.Length && !_escaped[seat])
            {
                _escaped[seat] = true;
                _escapeOrder.Add(seat);
            }
        }

        /// <summary>The engine counts seats upward (R4); escaped seats are skipped.</summary>
        private long? NextActiveSeat(int seat)
        {
            var count = _handCounts.Length;
            for (var step = 1; step <= count; step++)
            {
                var next = (seat + step) % count;
                if (!IsEscaped(next))
                {
                    return next;
                }
            }

            return null;
        }

        private Comparison<string> _sort = CardOrder.Compare;

        /// <summary>The hand order is cosmetic and client-side (R8); a new order re-sorts at once.</summary>
        public void SetSort(Comparison<string> sort)
        {
            _sort = sort;
            SortHand();
            Raise();
        }

        private void SortHand() => _hand.Sort(_sort);

        private void Raise() => Changed?.Invoke();
    }
}
