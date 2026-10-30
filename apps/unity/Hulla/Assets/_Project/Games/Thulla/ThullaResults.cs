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
            _share.text = "";
            _share.tooltip = UiText.ShareResult;
            _share.clicked += () => _onShare?.Invoke();
            _share.style.display = DisplayStyle.None;
            _fair = root.Q<Button>("result-fair");
            _fair.text = UiText.FairDealShort;
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
            _level = root.Q<VisualElement>("result-level");
            _levelFill = root.Q<VisualElement>("result-level-fill");
            _levelText = root.Q<Label>("result-level-text");
            _level.style.display = DisplayStyle.None;
            _leaveButton = root.Q<Button>("result-leave");
            // Underlined, a quiet link rather than a button (concept: Results): the words sit on a line of their own.
            _leaveButton.text = "";
            var leaveText = new Label(UiText.LeaveTable) { pickingMode = PickingMode.Ignore };
            leaveText.AddToClassList("results__leave-text");
            _leaveButton.Add(leaveText);
            _leaveButton.clicked += () => Leave?.Invoke();
        }

        private readonly VisualElement _level;
        private readonly VisualElement _levelFill;
        private readonly Label _levelText;
        private readonly Button _leaveButton;

        /// <summary>Leave the table, a plain plank at the bottom so nobody leaves by accident (design: Results).</summary>
        public Action? Leave { get; set; }

        /// <summary>The level bar under the reward line: the level reached and how far into the next.</summary>
        public void ShowLevel(long level, float fraction)
        {
            _level.style.display = DisplayStyle.Flex;
            _levelFill.style.width = Length.Percent(Math.Clamp(fraction, 0.04f, 1f) * 100f);
            _levelText.text = Strings.Format(UiText.LevelN, level);
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
            _share.tooltip = UiText.ShareBigThulla;
            _share.AddToClassList("results__share--hot");
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
            Hulla.Presentation.Theme.Backdrop.Set(_root, show ? Hulla.Presentation.Theme.BackdropKind.Felt : Hulla.Presentation.Theme.BackdropKind.Calm);
            // The results own the screen: the table underneath (seats, hand, strip) steps out of view.
            _root.parent?.EnableInClassList("table--results", show);
            if (result == null || !show)
            {
                return;
            }

            if (_bigThullaRound != null && _bigThullaRound != result.RoundId)
            {
                _bigThullaRound = null;
                _share.tooltip = UiText.ShareResult;
                _share.RemoveFromClassList("results__share--hot");
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

        /// <summary>
        /// The podium (concept: Results): drawn columns for second, first and third, the winner tallest in the
        /// middle under a crown, gold, silver and bronze tags, a name plate on each column, and the Bhabhi on
        /// the shortest column at the end, greyed under the stamp (GDD 14.8).
        /// </summary>
        private void Podium(TableModel model, RoundEnd result)
        {
            _podium.Clear();
            var stand = new VisualElement();
            stand.AddToClassList("podium__stand");
            _podium.Add(stand);
            var places = Math.Min(3, result.EscapeOrder.Count);
            var order = places == 3 ? new[] { 1, 0, 2 } : places == 2 ? new[] { 1, 0 } : places == 1 ? new[] { 0 } : Array.Empty<int>();
            foreach (var place in order)
            {
                var seat = result.EscapeOrder[place];
                stand.Add(Column(_nameOf(seat), _avatarOf(seat), seat == model.MySeat, "podium__col--" + (place + 1), Ordinal(place + 1), place == 0));
            }

            var loser = Column(_nameOf(result.BhabhiSeat), _avatarOf(result.BhabhiSeat), result.BhabhiSeat == model.MySeat, "podium__col--loser", null, false);
            // The stamp lands on the Bhabhi's own face (design: Results), not over the headline.
            _stamp.RemoveFromHierarchy();
            loser.Add(_stamp);
            stand.Add(loser);
        }

        private static VisualElement Column(string name, string? avatarId, bool mine, string cls, string? rank, bool crown)
        {
            var column = new VisualElement();
            column.AddToClassList("podium__place");
            column.AddToClassList("podium__col");
            column.AddToClassList(cls);
            if (crown)
            {
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("podium__crown");
                column.Add(top);
            }

            column.Add(Avatar(name, avatarId));
            var block = new VisualElement();
            block.AddToClassList("podium__block");
            if (rank != null)
            {
                var tag = new Label(rank);
                tag.AddToClassList("podium__rank");
                block.Add(tag);
            }

            var spacer = new VisualElement();
            spacer.AddToClassList("podium__spacer");
            block.Add(spacer);
            var plate = new Label(mine ? UiText.You : name);
            plate.AddToClassList("podium__name");
            block.Add(plate);
            column.Add(block);
            return column;
        }

        /// <summary>The face from the avatar set, else the tinted initial.</summary>
        private static VisualElement Avatar(string name, string? avatarId)
        {
            var avatar = new VisualElement();
            avatar.AddToClassList("seat__avatar");
            avatar.AddToClassList("podium__avatar");
            var initial = new Label();
            initial.AddToClassList("seat__initial");
            avatar.Add(initial);
            AvatarArt.Apply(avatar, initial, avatarId, name);
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
                    var avatar = Avatar(_nameOf(seat), _avatarOf(seat));
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

            // The countdown sits on the button and the accepted faces fill along it (design: Results).
            _playAgain.text = voted ? UiText.Waiting4Rematch : seconds >= 0 ? Strings.Get("ui.play_again_countdown", seconds) : UiText.PlayAgain;
            _status.text = "";
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

            var box = new VisualElement();
            box.AddToClassList("results__highlights");
            if (picker >= 0)
            {
                var line = new Label(Strings.Format(UiText.MostPickedUp, _nameOf(picker), biggest));
                line.AddToClassList("results__highlight");
                box.Add(line);
            }

            if (giver >= 0)
            {
                var line = new Label(Strings.Format(UiText.MostThullas, _nameOf(giver), thullas));
                line.AddToClassList("results__highlight");
                box.Add(line);
            }

            _body.Add(box);
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
                var chip = new Button { text = Strings.Format(UiText.AddNamePlus, seat.DisplayName ?? "?") };
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
                // "New here:" and the chips on one line (design: Results).
                var label = new Label(UiText.NewHere);
                label.AddToClassList("results__new-here");
                row.Insert(0, label);
                row.AddToClassList("results__friends");
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
