using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Protocol;

namespace Hulla.Application.Table
{
    /// <summary>
    /// The protocol session for one table (ARCHITECTURE 9.3). Public messages apply in seq order; a gap
    /// holds later messages and asks for a snapshot. Seat messages carry the current seq and apply once
    /// the public stream has reached it. Every applied message updates the model and joins the event queue.
    /// </summary>
    public sealed class TableClient : IDisposable
    {
        private readonly ITableGateway _gateway;
        private readonly SortedList<long, List<ServerMessage>> _held = new SortedList<long, List<ServerMessage>>();
        private long _lastSeq = -1;
        private bool _snapshotRequested;
        private long _clientSeq;

        /// <param name="firstClientSeq">
        /// Where this client's move numbers start. The server caches acks per seat and number for the round,
        /// so a client rebuilt after a reconnect must start above anything it sent before (the caller passes
        /// the wall clock in milliseconds).
        /// </param>
        public TableClient(ITableGateway gateway, TableTicket ticket, string userId, long firstClientSeq)
        {
            _gateway = gateway;
            _clientSeq = firstClientSeq;
            Ticket = ticket;
            Model = new TableModel(userId);
            _gateway.MessageReceived += Receive;
        }

        public TableTicket Ticket { get; }

        public TableModel Model { get; }

        public EventQueue Events { get; } = new EventQueue();

        public long LastSeq => _lastSeq;

        public void Receive(ServerMessage message)
        {
            switch (message.Payload)
            {
                case TableState state:
                    ApplyBaseline(message, state.LastSeq);
                    return;
                case Snapshot snapshot:
                    ApplyBaseline(message, snapshot.State.LastSeq);
                    return;
            }

            if (_lastSeq < 0)
            {
                Hold(message);
                return;
            }

            if (MessageTypes.IsPublic(message.Type))
            {
                if (message.Seq <= _lastSeq)
                {
                    return;
                }

                if (message.Seq > _lastSeq + 1)
                {
                    Hold(message);
                    RequestSnapshot();
                    return;
                }

                Apply(message);
                _lastSeq = message.Seq;
                Drain();
                if (_held.Count == 0)
                {
                    _snapshotRequested = false;
                }

                return;
            }

            if (message.Seq > _lastSeq)
            {
                Hold(message);
                return;
            }

            Apply(message);
        }

        public void SetReady(bool ready) => Send(MessageTypes.Ready, new Ready { ReadyReady = ready });

        public void StartRound() => Send(MessageTypes.StartRound, new StartRound());

        public void VoteRematch(bool yes) => Send(MessageTypes.VoteRematch, new VoteRematch { Yes = yes });

        /// <summary>Plays a card the server marked legal; returns false when it is not playable now.</summary>
        public bool PlayCard(string card)
        {
            if (!Model.IsLegal(card))
            {
                return false;
            }

            Model.MarkPending(card);
            _clientSeq++;
            Send(MessageTypes.PlayCard, new PlayCard { RoundId = Model.RoundId, ClientSeq = _clientSeq, Card = card });
            return true;
        }

        public bool TakeHand()
        {
            if (!Model.MyTurn || !Model.CanTakeHand)
            {
                return false;
            }

            _clientSeq++;
            Send(MessageTypes.TakeHand, new TakeHand { RoundId = Model.RoundId, ClientSeq = _clientSeq });
            return true;
        }

        public void Dispose() => _gateway.MessageReceived -= Receive;

        private void ApplyBaseline(ServerMessage message, long seq)
        {
            _snapshotRequested = false;
            _lastSeq = seq;

            // Anything held from before this baseline is already part of it; replaying a stale
            // turn_start would light cards for a turn that has passed.
            while (_held.Count > 0 && _held.Keys[0] <= seq)
            {
                _held.RemoveAt(0);
            }

            Apply(message);
            Drain();
        }

        private void Apply(ServerMessage message)
        {
            Model.Apply(message);
            Events.Enqueue(message);
            if (Model.NeedsSnapshotAfter(message.Payload))
            {
                RequestSnapshot();
            }
        }

        private void Hold(ServerMessage message)
        {
            if (!_held.TryGetValue(message.Seq, out var list))
            {
                list = new List<ServerMessage>();
                _held.Add(message.Seq, list);
            }

            list.Add(message);
        }

        private void Drain()
        {
            while (_held.Count > 0)
            {
                var seq = _held.Keys[0];
                var list = _held.Values[0];
                if (seq > _lastSeq + 1)
                {
                    return;
                }

                _held.RemoveAt(0);
                var advanced = false;
                foreach (var message in list)
                {
                    if (MessageTypes.IsPublic(message.Type))
                    {
                        if (message.Seq != _lastSeq + 1 || advanced)
                        {
                            continue;
                        }

                        Apply(message);
                        advanced = true;
                    }
                }

                if (advanced)
                {
                    _lastSeq = seq;
                }

                foreach (var message in list)
                {
                    if (!MessageTypes.IsPublic(message.Type) && message.Seq <= _lastSeq)
                    {
                        Apply(message);
                    }
                }
            }
        }

        private void RequestSnapshot()
        {
            if (_snapshotRequested)
            {
                return;
            }

            _snapshotRequested = true;
            Send(MessageTypes.RequestSnapshot, new RequestSnapshot { SinceSeq = _lastSeq });
        }

        private void Send(string type, object payload) => _gateway.Send(type, payload);
    }
}
