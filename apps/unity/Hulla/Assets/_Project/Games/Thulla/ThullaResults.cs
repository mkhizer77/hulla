using System;
using System.Collections.Generic;
using Hulla.Application.Table;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The results overlay (GDD 14.8, design §8): a podium of avatars in escape order, the loser with
    /// the stamp and the table's title, the room's points and Bhabhi tally, a highlight or two, the
    /// fair-deal chip that explains the shuffle, "Add friend" chips for co-players who are not friends
    /// yet, the rematch vote with the accepted avatars and its countdown (R52), and Change rules for
    /// the host. Nobody leaves the table unless they tap Leave.
    /// </summary>
    public sealed class ThullaResults
    {
        private readonly VisualElement _root;
        private readonly Label _title;
        private readonly Label _stamp;
        private readonly VisualElement _podium;
        private readonly VisualElement _body;
        private readonly Button _playAgain;
        private readonly VisualElement _votes;
        private readonly Label _status;
        private readonly Func<long, string> _nameOf;
        private readonly Func<long, string?> _avatarOf;
        private long _shownSeconds = -1;
        private readonly Label _reward;
        private readonly Button _share;
        private readonly Button _fair;
        private readonly Button _changeRules;
        private readonly Action _onPlayAgain;
        private const int MinimumPlayers = 3;
        private Action? _onShare;
        private Action? _onChangeRules;
        private Fairness? _fairness;
        private string? _bigThullaRound;

        public ThullaResults(VisualElement root, Func<long, string> nameOf, Func<long, string?> avatarOf, Action playAgain)
        {
            _root = root;
            _nameOf = nameOf;
            _avatarOf = avatarOf;
            _title = root.Q<Label>("result-title");
            _stamp = root.Q<Label>("result-stamp");
            _podium = root.Q<VisualElement>("result-podium");
            _body = root.Q<VisualElement>("result-order");
            _votes = root.Q<VisualElement>("result-votes");
            _playAgain = root.Q<Button>("play-again");
            _playAgain.text = UiText.PlayAgain;
            _playAgain.clicked += playAgain;
            _onPlayAgain = playAgain;
            _status = root.Q<Label>("result-status");
            _reward = root.Q<Label>("result-reward");
            _share = root.Q<Button>("result-share");
            _share.text = UiText.ShareResult;
            _share.clicked += () => _onShare?.Invoke();
            _share.style.display = DisplayStyle.None;
            _fair = root.Q<Button>("result-fair");
            _fair.text = UiText.FairDealCheck;
            _fair.clicked += () =>
            {
                if (_fairness != null)
                {
                    ShowFairness?.Invoke(_fairness);
                }
            };
            _changeRules = root.Q<Button>("change-rules");
            _changeRules.text = UiText.ChangeRules;
            _changeRules.clicked += () => _onChangeRules?.Invoke();
            _changeRules.style.display = DisplayStyle.None;
        }

        /// <summary>Shares the result as a card (GDD 10.9); null hides the button (the tutorial).</summary>
        public Action? Share
        {
            get => _onShare;
            set
            {
                _onShare = value;
                _share.style.display = value != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// The night's biggest thulla (GDD 10.9) turns Share into a highlighted "Share the biggest
        /// thulla" for that round, in the panel rather than a dialog over it (GDD 14.5).
        /// </summary>
        public void OfferBigThulla(string roundId)
        {
            _bigThullaRound = roundId;
            _share.text = UiText.ShareBigThulla;
            _share.RemoveFromClassList("button--quiet");
            _share.AddToClassList("button--secondary");
        }

        /// <summary>Opens the fair-deal explanation (ADR-0009); null hides the chip.</summary>
        public Action<Fairness>? ShowFairness { get; set; }

        /// <summary>The host edits the rules from here (design §8); null hides the button.</summary>
        public Action? ChangeRules
        {
            get => _onChangeRules;
            set => _onChangeRules = value;
        }

        /// <summary>The round's XP, coins and any level-up (GDD 14.8), once the server has paid it.</summary>
        public void ShowReward(string? text) => _reward.text = text ?? "";

        /// <summary>Who may get an "Add friend" chip; null hides the chips.</summary>
        public Func<string, bool>? CanAdd { get; set; }

        public Action<string>? AddFriend { get; set; }

        /// <summary>The names of players who left during the round; a bot finished it for them (R44).</summary>
        public Func<IReadOnlyList<string>>? WhoLeft { get; set; }

        /// <summary>Shares the room code so someone can take a free seat; shown at private tables only.</summary>
        public Action? Invite { get; set; }

        public void Show(TableModel model, bool voted)
        {
            var result = model.LastResult;
            var show = result != null && !model.InRound;
            _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (result == null || !show)
            {
                return;
            }

            if (_bigThullaRound != null && _bigThullaRound != result.RoundId)
            {
                _bigThullaRound = null;
                _share.text = UiText.ShareResult;
                _share.RemoveFromClassList("button--secondary");
                _share.AddToClassList("button--quiet");
            }

            _fairness = result.Fairness;
            _fair.style.display = ShowFairness != null && _fairness != null ? DisplayStyle.Flex : DisplayStyle.None;
            _changeRules.style.display = _onChangeRules != null && model.IsHost && model.IsPrivate ? DisplayStyle.Flex : DisplayStyle.None;
            _title.text = Strings.Format(UiText.LoserIs, _nameOf(result.BhabhiSeat), LoserTitle(model));
            _stamp.text = LoserTitle(model).ToUpperInvariant();
            Podium(model, result);
            _body.Clear();
            if (result.EscapeOrder.Count > 3)
            {
                Heading(UiText.EscapeOrder);
                for (var i = 3; i < result.EscapeOrder.Count; i++)
                {
                    Line((i + 1) + ". " + _nameOf(result.EscapeOrder[i]), null);
                }
            }

            var series = result.Series;
            if (series != null && series.RoundsPlayed > 1)
            {
                Heading(Strings.Format(UiText.AfterRounds, series.RoundsPlayed));
                for (var seat = 0; seat < series.Points.Count; seat++)
                {
                    var tally = seat < series.LoserTally.Count ? series.LoserTally[seat] : 0;
                    Line(Strings.Format(UiText.PointsLine, _nameOf(seat), series.Points[seat], tally, LoserTitle(model)), null);
                }
            }

            Highlights(result);
            LeftPlayers(model, voted);
            FriendChips(model);
            _playAgain.SetEnabled(!voted);
            _shownSeconds = -1;
            UpdateStatus(model, voted, 0);
        }

        /// <summary>The first three out on a podium, the loser beside them with the stamp (GDD 14.8).</summary>
        private void Podium(TableModel model, RoundEnd result)
        {
            _podium.Clear();
            var places = Math.Min(3, result.EscapeOrder.Count);
            // Second, first, third: the winner stands in the middle and tallest.
            var order = places == 3 ? new[] { 1, 0, 2 } : places == 2 ? new[] { 1, 0 } : places == 1 ? new[] { 0 } : Array.Empty<int>();
            foreach (var place in order)
            {
                var seat = result.EscapeOrder[place];
                var column = new VisualElement();
                column.AddToClassList("podium__place");
                column.AddToClassList("podium__place--" + (place + 1));
                column.Add(Avatar(_nameOf(seat), _avatarOf(seat), seat == model.MySeat));
                var name = new Label(_nameOf(seat));
                name.AddToClassList("podium__name");
                column.Add(name);
                var rank = new Label(Ordinal(place + 1));
                rank.AddToClassList("podium__rank");
                column.Add(rank);
                _podium.Add(column);
            }

            var loser = new VisualElement();
            loser.AddToClassList("podium__place");
            loser.AddToClassList("podium__place--loser");
            loser.Add(Avatar(_nameOf(result.BhabhiSeat), _avatarOf(result.BhabhiSeat), result.BhabhiSeat == model.MySeat));
            var loserName = new Label(_nameOf(result.BhabhiSeat));
            loserName.AddToClassList("podium__name");
            loser.Add(loserName);
            _podium.Add(loser);
        }

        /// <summary>The face from the avatar set, else the tinted initial; the player's own carries the "You" tag.</summary>
        private static VisualElement Avatar(string name, string? avatarId, bool mine)
        {
            var avatar = new VisualElement();
            avatar.AddToClassList("seat__avatar");
            avatar.AddToClassList("podium__avatar");
            avatar.EnableInClassList("podium__avatar--me", mine);
            var initial = new Label();
            initial.AddToClassList("seat__initial");
            avatar.Add(initial);
            AvatarArt.Apply(avatar, initial, avatarId, name);
            if (mine)
            {
                var you = new Label(UiText.You);
                you.AddToClassList("seat__you");
                avatar.Add(you);
            }

            return avatar;
        }

        public static string Ordinal(int place) => Strings.Get("ui.ordinal", place);

        /// <summary>Per frame while the overlay shows: the rematch countdown and the accepted avatars.</summary>
        public void Tick(TableModel model, bool voted, long nowMs)
        {
            if (_root.style.display == DisplayStyle.Flex)
            {
                UpdateStatus(model, voted, nowMs);
            }
        }

        private void UpdateStatus(TableModel model, bool voted, long nowMs)
        {
            var deadline = model.RematchDeadlineTs;
            var seconds = deadline == null || nowMs == 0 ? -1 : Math.Max(0, (deadline.Value - nowMs + 999) / 1000);
            var votes = model.RematchVotes.Count;
            if (votes != _votes.childCount)
            {
                _votes.Clear();
                foreach (var seat in model.RematchVotes)
                {
                    var avatar = Avatar(_nameOf(seat), _avatarOf(seat), seat == model.MySeat);
                    avatar.AddToClassList("podium__avatar--vote");
                    _votes.Add(avatar);
                }
            }

            if (seconds == _shownSeconds && nowMs != 0)
            {
                return;
            }

            _shownSeconds = seconds;
            var players = 0;
            foreach (var seat in model.Seats)
            {
                players += seat.Occupied ? 1 : 0;
            }

            var text = voted ? UiText.Waiting4Rematch : votes == 0 ? UiText.RematchHow : "";
            if (votes > 0)
            {
                text = (text.Length > 0 ? text + " " : "") + Strings.Format(UiText.RematchVotes, votes, players);
            }

            _playAgain.text = seconds >= 0 ? Strings.Get("ui.play_again_countdown", seconds) : UiText.PlayAgain;
            _status.text = text;
        }

        private void Highlights(RoundEnd result)
        {
            long biggest = 0;
            long picker = -1;
            long thullas = 0;
            long giver = -1;
            for (var seat = 0; seat < result.Stats.Count; seat++)
            {
                var stats = result.Stats[seat];
                if (stats.CardsPickedUp > biggest)
                {
                    biggest = stats.CardsPickedUp;
                    picker = seat;
                }

                if (stats.ThullasGiven > thullas)
                {
                    thullas = stats.ThullasGiven;
                    giver = seat;
                }
            }

            if (picker < 0 && giver < 0)
            {
                return;
            }

            Heading(UiText.Highlights);
            if (giver >= 0)
            {
                Line(Strings.Format(UiText.MostThullas, _nameOf(giver), thullas), null);
            }

            if (picker >= 0)
            {
                Line(Strings.Format(UiText.MostPickedUp, _nameOf(picker), biggest), null);
            }
        }

        /// <summary>
        /// Who left mid-round, and what the table can do about it: invite someone into the seat
        /// (private rooms, by the room code) or play on without them, which needs three players.
        /// </summary>
        private void LeftPlayers(TableModel model, bool voted)
        {
            var left = WhoLeft?.Invoke();
            if (left == null || left.Count == 0)
            {
                return;
            }

            Heading(Strings.Format(UiText.LeftTheTable, string.Join(", ", left)));
            var stayed = 0;
            foreach (var seat in model.Seats)
            {
                stayed += seat.Occupied && seat.Connected ? 1 : 0;
            }

            var row = new VisualElement();
            row.AddToClassList("chips");
            if (Invite != null && model.IsPrivate)
            {
                var invite = new Button { text = UiText.InviteSomeone };
                invite.AddToClassList("chip");
                invite.clicked += Invite;
                row.Add(invite);
            }

            if (stayed >= MinimumPlayers)
            {
                var carryOn = new Button { text = UiText.ContinueWithout };
                carryOn.AddToClassList("chip");
                carryOn.SetEnabled(!voted);
                carryOn.clicked += _onPlayAgain;
                row.Add(carryOn);
            }
            else
            {
                Line(model.IsPrivate ? UiText.NeedThreeInvite : UiText.NeedThreeFindTable, null);
            }

            if (row.childCount > 0)
            {
                _body.Add(row);
            }
        }

        private void FriendChips(TableModel model)
        {
            if (CanAdd == null || AddFriend == null)
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("chips");
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied || seat.Seat == model.MySeat || seat.PlayerId == null || !CanAdd(seat.PlayerId))
                {
                    continue;
                }

                var id = seat.PlayerId;
                var chip = new Button { text = Strings.Format(UiText.AddName, seat.DisplayName ?? "?") };
                chip.AddToClassList("chip");
                chip.clicked += () =>
                {
                    AddFriend(id);
                    chip.SetEnabled(false);
                    chip.text = UiText.RequestSent;
                };
                row.Add(chip);
            }

            if (row.childCount > 0)
            {
                Heading(UiText.MakeFriends);
                _body.Add(row);
            }
        }

        private void Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("caption");
            label.AddToClassList("settings__heading");
            _body.Add(label);
        }

        private void Line(string text, string? cls)
        {
            var label = new Label(text);
            label.AddToClassList("rulebook__line");
            if (cls != null)
            {
                label.AddToClassList(cls);
            }

            _body.Add(label);
        }

        private static string LoserTitle(TableModel model) =>
            (model.Selection?.Settings?.LoserTitle ?? Protocol.LoserTitle.Bhabhi) switch
            {
                Protocol.LoserTitle.Donkey => RulebookText.Donkey,
                Protocol.LoserTitle.Thullad => RulebookText.Thullad,
                _ => RulebookText.Bhabhi,
            };
    }
}
