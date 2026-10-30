using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Application.Tutorial
{
    /// <summary>
    /// The tutorial's stand-in for the server (ARCHITECTURE 9.4, ADR-0019): it plays a scripted step as
    /// the same messages a real table sends, so the normal table client, model and table kit show it.
    /// Scripted opponents are part of the script, not bots (GDD 7.1). The player's turn waits for a
    /// card the script allows; one wrong tap earns a hint, a second one and the coach plays it (17.3).
    /// Time comes in through <see cref="Tick"/>, so the script is testable without a clock.
    /// </summary>
    public sealed class ScriptedTableGateway : ITableGateway
    {
        /// <summary>The pause between scripted plays, so each one can be seen.</summary>
        public const float BeatSeconds = 0.8f;

        private const string RoundId = "tutorial";

        private readonly IReadOnlyList<TutorialStep> _steps;
        private readonly Func<long> _nowMs;
        private readonly List<string> _hand = new List<string>();
        private readonly List<(long Seat, string Card)> _trick = new List<(long Seat, string Card)>();
        private long[] _counts = new long[0];
        private readonly List<long> _escapeOrder = new List<long>();
        private int _step = -1;
        private int _beat;
        private float _wait;
        private bool _waitingForMe;
        private IReadOnlyList<string> _legal = Array.Empty<string>();
        private bool _myThulla;
        private int _wrongTaps;
        private bool _hinted;
        private bool _autoPlayed;
        private float _stepSeconds;
        private long _seq;
        private long _trickIndex;
        private bool _stepDone;

        private readonly string _label;

        /// <param name="label">Shown where a real table shows its room code.</param>
        public ScriptedTableGateway(IReadOnlyList<TutorialStep> steps, Func<long> nowMs, string label = "")
        {
            _label = label;
            _steps = steps;
            _nowMs = nowMs;
        }

        public event Action<ServerMessage>? MessageReceived;

        /// <summary>A script never drops.</summary>
        public event Action? Disconnected
        {
            add { }
            remove { }
        }

        /// <summary>A coach line to show over the table.</summary>
        public event Action<string>? CoachSaid;

        /// <summary>A step ended: its id, outcome (clean, hinted or coached) and seconds (the ftue_step event, GDD 17.6).</summary>
        public event Action<string, string, float>? StepFinished;

        public int StepIndex => _step;

        /// <summary>The player's own face at the scripted table; null keeps the initial.</summary>
        public string? MyAvatar { get; set; }

        /// <summary>Faces for the scripted seats, so the coach's table looks like a real one.</summary>
        private static readonly string[] CoachAvatars = { "a04", "a06", "a02", "a05", "a07" };

        public int StepCount => _steps.Count;

        public bool StepDone => _stepDone;

        public bool HasNextStep => _step + 1 < _steps.Count;

        /// <summary>The step's closing line, shown with a Next button once it ends.</summary>
        public string DoneText => _step >= 0 && _step < _steps.Count ? _steps[_step].Done : "";

        public bool WaitingForMe => _waitingForMe;

        public void BeginNextStep()
        {
            _step++;
            if (_step >= _steps.Count)
            {
                return;
            }

            var step = _steps[_step];
            _hand.Clear();
            _hand.AddRange(step.MyHand);
            _counts = new long[step.HandCounts.Count];
            for (var i = 0; i < _counts.Length; i++)
            {
                _counts[i] = step.HandCounts[i];
            }

            _counts[0] = _hand.Count;
            _trick.Clear();
            _escapeOrder.Clear();
            _escapeOrder.AddRange(step.Escaped);
            _beat = 0;
            _wait = 0f;
            _waitingForMe = false;
            _wrongTaps = 0;
            _hinted = false;
            _autoPlayed = false;
            _stepSeconds = 0f;
            _stepDone = false;
            _trickIndex = 0;
            Emit(MessageTypes.TableState, TableState(step), advance: true);
        }

        public void Tick(float deltaSeconds)
        {
            if (_step < 0 || _step >= _steps.Count || _stepDone)
            {
                return;
            }

            _stepSeconds += deltaSeconds;
            if (_waitingForMe)
            {
                return;
            }

            _wait -= deltaSeconds;
            while (_wait <= 0f && !_waitingForMe && !_stepDone)
            {
                RunBeat();
            }
        }

        /// <summary>The player tapped a card the script doesn't allow: a hint first, then the coach plays.</summary>
        public void WrongTap()
        {
            if (!_waitingForMe)
            {
                return;
            }

            _wrongTaps++;
            var beat = _steps[_step].Beats[_beat];
            if (_wrongTaps == 1)
            {
                _hinted = true;
                CoachSaid?.Invoke(beat.Text);
                return;
            }

            _autoPlayed = true;
            PlayMine(_legal[0], null);
        }

        public void Send(string type, object payload)
        {
            if (type != MessageTypes.PlayCard || payload is not PlayCard play || !_waitingForMe)
            {
                return;
            }

            if (!Contains(_legal, play.Card))
            {
                Emit(MessageTypes.MoveRejected, new MoveRejected { RoundId = RoundId, ClientSeq = play.ClientSeq, Error = new RuleError { Code = "rule.not_legal" } }, advance: false);
                WrongTap();
                return;
            }

            PlayMine(play.Card, play.ClientSeq);
        }

        public Awaitable<Result<TableTicket>> CreateTableAsync(CancellationToken cancellation) => Done(Result<TableTicket>.Fail("tutorial"));

        public Awaitable<Result<TableTicket>> JoinByCodeAsync(string code, CancellationToken cancellation) => Done(Result<TableTicket>.Fail("tutorial"));

        public Awaitable<Result<TableTicket?>> FindMineAsync(CancellationToken cancellation) => Done(Result<TableTicket?>.Ok(null));

        public Awaitable<Result<bool>> EnterAsync(TableTicket ticket, CancellationToken cancellation) => Done(Result<bool>.Ok(true));

        public Awaitable LeaveAsync()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        private static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private static bool Contains(IReadOnlyList<string> cards, string card)
        {
            foreach (var c in cards)
            {
                if (c == card)
                {
                    return true;
                }
            }

            return false;
        }

        private void RunBeat()
        {
            var beats = _steps[_step].Beats;
            if (_beat >= beats.Count)
            {
                FinishStep();
                return;
            }

            var beat = beats[_beat];
            switch (beat.Kind)
            {
                case ScriptBeat.BeatKind.Coach:
                    CoachSaid?.Invoke(beat.Text);
                    _beat++;
                    _wait = BeatSeconds;
                    return;
                case ScriptBeat.BeatKind.Play:
                    PlayCard(beat.Seat, beat.Card, beat.Thulla, null);
                    _beat++;
                    _wait = BeatSeconds;
                    return;
                case ScriptBeat.BeatKind.MyTurn:
                    _legal = LegalFor(beat);
                    _myThulla = beat.Thulla;
                    _waitingForMe = true;
                    _wrongTaps = 0;
                    Emit(MessageTypes.TurnStart, new TurnStart { RoundId = RoundId, Seat = 0, Legal = new List<string>(_legal), DeadlineTs = _nowMs() + 600_000 }, advance: false);
                    return;
                case ScriptBeat.BeatKind.Resolve:
                    Resolve(beat);
                    _beat++;
                    _wait = BeatSeconds * 1.5f;
                    return;
                case ScriptBeat.BeatKind.Escape:
                    _escapeOrder.Add(beat.Seat);
                    Emit(MessageTypes.Escape, new Escape { RoundId = RoundId, Seat = beat.Seat, Rank = _escapeOrder.Count }, advance: true);
                    _beat++;
                    _wait = BeatSeconds;
                    return;
                case ScriptBeat.BeatKind.Draw:
                    _counts[beat.Seat]++;
                    _counts[beat.From]--;
                    if (beat.From == 0)
                    {
                        _hand.Remove(beat.Card);
                    }

                    Emit(MessageTypes.PrivateDraw, new PrivateDraw { RoundId = RoundId, Seat = beat.Seat, Card = beat.Card, Source = Source.Opponent, From = beat.From, AutoLead = true }, advance: false);
                    _beat++;
                    _wait = BeatSeconds * 1.5f;
                    return;
            }
        }

        private IReadOnlyList<string> LegalFor(ScriptBeat beat)
        {
            if (beat.Legal != null)
            {
                return beat.Legal;
            }

            var legal = new List<string>();
            if (beat.Suit != null)
            {
                foreach (var card in _hand)
                {
                    if (card[1] == beat.Suit.Value)
                    {
                        legal.Add(card);
                    }
                }
            }

            if (legal.Count == 0)
            {
                legal.AddRange(_hand);
            }

            return legal;
        }

        private void PlayMine(string card, long? clientSeq)
        {
            _waitingForMe = false;
            _hand.Remove(card);
            PlayCard(0, card, _myThulla, clientSeq);
            _beat++;
            _wait = BeatSeconds;
        }

        private void PlayCard(long seat, string card, bool thulla, long? clientSeq)
        {
            _counts[seat]--;
            _trick.Add((seat, card));
            Emit(MessageTypes.MoveApplied, new MoveApplied
            {
                RoundId = RoundId,
                Seat = seat,
                Move = new Move { Type = TypeEnum.PlayCard, Card = card },
                TrickIndex = _trickIndex,
                Thulla = thulla,
                ClientSeq = clientSeq,
                Auto = clientSeq == null && seat == 0,
            }, advance: true);
        }

        private void Resolve(ScriptBeat beat)
        {
            var cards = new List<string>();
            foreach (var play in _trick)
            {
                cards.Add(play.Card);
            }

            if (beat.Picker != null)
            {
                _counts[beat.Picker.Value] += cards.Count;
                if (beat.Picker == 0)
                {
                    _hand.AddRange(cards);
                }
            }

            long? thullaBy = null;
            var led = _trick.Count > 0 ? _trick[0].Card[1] : 'S';
            foreach (var play in _trick)
            {
                if (play.Card[1] != led)
                {
                    thullaBy = play.Seat;
                }
            }

            Emit(MessageTypes.TrickResolved, new TrickResolved
            {
                RoundId = RoundId,
                TrickIndex = _trickIndex,
                LedSuit = SuitOf(led),
                Cards = cards,
                ThullaBy = beat.Picker == null ? null : thullaBy,
                Picker = beat.Picker,
                NextLeader = beat.NextLeader,
                HandCounts = new List<long>(_counts),
                Escapes = new List<Escape>(),
            }, advance: true);
            if (beat.Picker != null)
            {
                Emit(MessageTypes.Pickup, new Pickup { RoundId = RoundId, Seat = beat.Picker.Value, Cards = cards, TrickIndex = _trickIndex }, advance: true);
            }

            _trick.Clear();
            _trickIndex++;
        }

        private void FinishStep()
        {
            _stepDone = true;
            var outcome = _autoPlayed ? "coached" : _hinted ? "hinted" : "clean";
            StepFinished?.Invoke(_steps[_step].Id, outcome, _stepSeconds);
        }

        private TableState TableState(TutorialStep step)
        {
            var seats = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < step.Names.Count; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == 0 ? "me" : "coach-" + i, DisplayName = step.Names[i], Connected = true, Ready = true, Avatar = i == 0 ? MyAvatar : CoachAvatars[(i - 1) % CoachAvatars.Length] });
                views.Add(new SeatView
                {
                    HandCount = _counts[i],
                    Hand = i == 0 ? new List<string>(_hand) : null,
                    Escaped = Contains(step.Escaped, i),
                });
            }

            return new TableState
            {
                Code = _label,
                Mode = TableMode.Private,
                Status = TableStatus.InRound,
                LastSeq = _seq + 1,
                Seats = seats,
                Rules = new RulesSelection { GameId = GameId.Thulla, Preset = Preset.Standard },
                Round = new View
                {
                    Phase = Phase.Play,
                    SeatCount = step.Names.Count,
                    Seats = views,
                    EscapeOrder = new List<long>(step.Escaped),
                    Trick = new Trick { Plays = new List<Play>() },
                    TurnSeat = step.Leader,
                },
            };
        }

        private static bool Contains(IReadOnlyList<long> seats, long seat)
        {
            foreach (var s in seats)
            {
                if (s == seat)
                {
                    return true;
                }
            }

            return false;
        }

        private static Suit SuitOf(char suit) => suit switch
        {
            'H' => Protocol.Suit.H,
            'C' => Protocol.Suit.C,
            'D' => Protocol.Suit.D,
            _ => Protocol.Suit.S,
        };

        /// <summary>Public messages advance the seq like a real table; seat messages carry the current one.</summary>
        private void Emit(string type, object payload, bool advance)
        {
            if (advance)
            {
                _seq++;
            }

            MessageReceived?.Invoke(new ServerMessage(type, _seq, payload));
        }
    }
}
