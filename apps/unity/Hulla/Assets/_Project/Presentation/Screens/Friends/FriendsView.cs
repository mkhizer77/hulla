using System;
using System.Collections.Generic;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Friends
{
    /// <summary>
    /// Friends (design: Friends), ordered by what you can do now: Playing now (Join or Watch),
    /// Requests (Accept or ✕), Played with recently (Add), Online (Invite), then everyone else. One
    /// action per row; a tap on the row opens the player sheet with Favourite, Remove and Block.
    /// The + opens the add-friend sheet; the search field filters every group (GDD 10.3).
    /// </summary>
    public sealed class FriendsView : IDisposable
    {
        private readonly FriendsViewModel _model;
        private readonly VisualElement _root;
        private readonly VisualElement _list;
        private readonly Label _status;
        private readonly TextField _search;
        private readonly Actions _actions;

        private readonly Action? _retry;
        private readonly Action? _showMyCode;

        /// <param name="retry">Loads the lists again after an error; null hides Retry.</param>
        /// <param name="showMyCode">Shares the friend code from the empty state.</param>
        public FriendsView(VisualElement root, FriendsViewModel model, Action back, Actions actions, Action? retry = null, Action? showMyCode = null)
        {
            _model = model;
            _root = root;
            _actions = actions;
            _retry = retry;
            _showMyCode = showMyCode;
            root.Q<Label>("title").text = UiText.Friends;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            var addOpen = root.Q<Button>("add-open");
            if (addOpen != null)
            {
                addOpen.text = "";
                addOpen.tooltip = UiText.AddFriendTitle;
                addOpen.clicked += OpenAddSheet;
            }

            _search = root.Q<TextField>("search");
            _search.textEdition.placeholder = UiText.SearchFriends;
            _search.RegisterValueChangedCallback(_ => Refresh());
            _status = root.Q<Label>("status");
            _list = root.Q<VisualElement>("list");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose()
        {
            _model.Changed -= Refresh;
            if (_suggestions != null)
            {
                _suggestions.Changed -= Refresh;
            }
        }

        private FriendSuggestionsViewModel? _suggestions;
        private Action? _findContacts;
        private Action? _findFacebook;
        private Action<string>? _addSuggestion;

        /// <summary>
        /// People you may know (concept: friends-suggestions, 30 Oct): Find your friends with From contacts
        /// and From Facebook, then the players found, each tagged with where they were found and an Add.
        /// </summary>
        public void UseSuggestions(FriendSuggestionsViewModel suggestions, Action findContacts, Action findFacebook, Action<string> add)
        {
            _suggestions = suggestions;
            _findContacts = findContacts;
            _findFacebook = findFacebook;
            _addSuggestion = add;
            suggestions.Changed += Refresh;
            Refresh();
        }

        private void BuildSuggestions()
        {
            var model = _suggestions;
            if (model == null || (!model.ContactsSupported && !model.FacebookSupported))
            {
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("find-card");
            var title = new Label(UiText.FindYourFriends);
            title.AddToClassList("find-card__title");
            card.Add(title);
            var buttons = new VisualElement();
            buttons.AddToClassList("find-card__buttons");
            if (model.ContactsSupported)
            {
                buttons.Add(FindButton(model.ContactsOn ? UiText.ContactsChecked : UiText.FromContacts, "contacts", () => _findContacts?.Invoke()));
            }

            if (model.FacebookSupported)
            {
                buttons.Add(FindButton(model.FacebookOn ? UiText.FacebookChecked : UiText.FromFacebook, "facebook", () => _findFacebook?.Invoke()));
            }

            card.Add(buttons);
            if (model.Busy)
            {
                card.Add(Caption(UiText.Looking));
            }
            else if (model.ErrorCode != null)
            {
                card.Add(Caption(UiText.Error(model.ErrorCode)));
            }
            else if ((model.ContactsOn || model.FacebookOn) && model.Suggestions.Count == 0)
            {
                card.Add(Caption(UiText.NobodyYet));
            }

            _list.Add(card);
            if (model.Suggestions.Count == 0)
            {
                return;
            }

            var heading = new VisualElement();
            heading.AddToClassList("friends__heading");
            var words = new Label(UiText.PeopleYouMayKnow);
            words.AddToClassList("caption");
            words.AddToClassList("settings__heading");
            words.AddToClassList("friends__heading-text");
            heading.Add(words);
            _list.Add(heading);
            foreach (var suggestion in model.Suggestions)
            {
                var entry = suggestion;
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                row.AddToClassList("friend-row");
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("friend-row__face");
                var initial = new Label { pickingMode = PickingMode.Ignore };
                initial.AddToClassList("seat__initial");
                face.Add(initial);
                AvatarArt.Apply(face, initial, entry.Avatar ?? AvatarArt.ForAccount(entry.UserId), entry.Name);
                row.Add(face);
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("lobby-seat__name");
                text.Add(new Label(entry.Name));
                var facebook = entry.Source == Hulla.Application.Social.SuggestionSource.Facebook;
                var chip = new Label(facebook ? UiText.FacebookFriend : UiText.InYourContacts);
                chip.AddToClassList("suggestion-chip");
                chip.AddToClassList(facebook ? "suggestion-chip--facebook" : "suggestion-chip--contacts");
                text.Add(chip);
                row.Add(text);
                row.Add(model.Requested(entry.UserId)
                    ? Quiet(UiText.Requested, () => { })
                    : Primary(UiText.Add, () => _addSuggestion?.Invoke(entry.UserId)));
                _list.Add(row);
            }
        }

        private static Button FindButton(string text, string key, Action tap)
        {
            var button = new Button(tap) { text = "" };
            button.AddToClassList("find-button");
            button.AddToClassList("find-button--" + key);
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("find-button__mark");
            button.Add(mark);
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("find-button__text");
            button.Add(label);
            return button;
        }

        private static Label Caption(string text)
        {
            var label = new Label(text);
            label.AddToClassList("find-card__note");
            return label;
        }

        /// <summary>Where a friend is now (Home presence); null treats everyone as on Home or offline.</summary>
        public Func<string, FriendPresence?>? PresenceOf { get; set; }

        /// <summary>Joins or watches a friend's table by its code.</summary>
        public Action<string>? Join { get; set; }

        public Action<string>? WatchTable { get; set; }

        /// <summary>Opens a room and invites the friend to it.</summary>
        public Action<string>? InviteToMine { get; set; }

        /// <summary>The player's own friend code for the add sheet.</summary>
        public Func<string?>? MyCode { get; set; }

        private void Refresh()
        {
            _status.text = _model.RequestSent ? UiText.RequestSent : "";
            _list.Clear();
            var nothingYet = _model.Received.Count + _model.Friends.Count + _model.Sent.Count + _model.Recent.Count + _model.Blocked.Count == 0;
            if (_model.ErrorCode != null && nothingYet)
            {
                ListState.Error(_list, _model.ErrorCode, _retry);
                return;
            }

            if (_model.Busy && nothingYet)
            {
                ListState.Loading(_list);
                return;
            }

            if (_model.ErrorCode != null)
            {
                _status.text = UiText.Error(_model.ErrorCode);
            }

            BuildSuggestions();
            if (_model.Friends.Count + _model.Received.Count + _model.Recent.Count + _model.Sent.Count == 0 && (_suggestions?.Suggestions.Count ?? 0) == 0)
            {
                ListState.Empty(_list, UiText.FriendsEmpty, _showMyCode == null ? null : UiText.Share, _showMyCode);
                return;
            }

            var filter = _search.value.Trim();
            var playing = new List<FriendEntry>();
            var online = new List<FriendEntry>();
            var rest = new List<FriendEntry>();
            foreach (var friend in _model.Friends)
            {
                if (!Matches(friend, filter))
                {
                    continue;
                }

                var presence = PresenceOf?.Invoke(friend.UserId);
                if (presence?.TableCode != null && (presence.Joinable || presence.Watchable))
                {
                    playing.Add(friend);
                }
                else if (friend.Online)
                {
                    online.Add(friend);
                }
                else
                {
                    rest.Add(friend);
                }
            }

            Section(UiText.PlayingNow, playing, friend =>
            {
                var presence = PresenceOf?.Invoke(friend.UserId);
                var code = presence?.TableCode ?? "";
                return presence != null && presence.Joinable
                    ? Primary(UiText.JoinTable, () => Join?.Invoke(code))
                    : Quiet(UiText.Watch, () => WatchTable?.Invoke(code));
            }, friend =>
            {
                var presence = PresenceOf?.Invoke(friend.UserId);
                return presence != null && presence.Joinable ? Strings.Format(UiText.AtTableSeats, presence.OpenSeats) : UiText.InARound;
            });
            Section(UiText.Requests, Filter(_model.Received, filter), friend => Primary(UiText.Accept, () => _actions.Add(friend.UserId)), friend => friend.Handle != null ? "@" + friend.Handle : "", friend => Dismiss(() => _actions.Remove(friend.UserId)), counted: true);
            Section(UiText.Recent, Filter(_model.Recent, filter), friend => Primary(UiText.Add, () => _actions.Add(friend.UserId)), friend => Strings.Get("ui.rounds_together", friend.Rounds));
            Section(UiText.Online, online, friend => Teal(UiText.Invite, () => InviteToMine?.Invoke(friend.UserId)), _ => UiText.OnHome);
            Section(UiText.SentRequests, Filter(_model.Sent, filter), friend => Quiet(UiText.CancelRequest, () => _actions.Remove(friend.UserId)), friend => friend.Handle != null ? "@" + friend.Handle : "");
            Section(Strings.Format(UiText.EveryoneElse, rest.Count), rest, null, _ => UiText.Offline);
        }

        private static bool Matches(FriendEntry entry, string filter) =>
            filter.Length == 0 || entry.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;

        private static List<FriendEntry> Filter(IReadOnlyList<FriendEntry> entries, string filter)
        {
            var list = new List<FriendEntry>();
            foreach (var entry in entries)
            {
                if (Matches(entry, filter))
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        private void Section(string title, IReadOnlyList<FriendEntry> entries, Func<FriendEntry, Button>? action, Func<FriendEntry, string> detail, Func<FriendEntry, Button>? extra = null, bool counted = false)
        {
            if (entries.Count == 0)
            {
                return;
            }

            // The heading's words and the count side by side: a label's own text ignores its children.
            var heading = new VisualElement();
            heading.AddToClassList("friends__heading");
            var words = new Label(title);
            words.AddToClassList("caption");
            words.AddToClassList("settings__heading");
            words.AddToClassList("friends__heading-text");
            heading.Add(words);
            _list.Add(heading);
            if (counted)
            {
                var count = new Label(Strings.Format("{0}", entries.Count));
                count.AddToClassList("friends__count");
                heading.Add(count);
            }

            foreach (var entry in entries)
            {
                var row = Row(entry, detail(entry));
                if (action != null)
                {
                    row.Add(action(entry));
                }

                if (extra != null)
                {
                    row.Add(extra(entry));
                }

                _list.Add(row);
            }
        }

        /// <summary>A friend's row: face with the online dot, name with the star, and the line under it.</summary>
        private VisualElement Row(FriendEntry entry, string detail)
        {
            var row = new VisualElement();
            row.AddToClassList("lobby-seat");
            row.AddToClassList("friend-row");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("friend-row__face");
            var initial = new Label { pickingMode = PickingMode.Ignore };
            initial.AddToClassList("seat__initial");
            face.Add(initial);
            AvatarArt.Apply(face, initial, AvatarArt.ForAccount(entry.UserId), entry.Name);
            if (entry.Online)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("friend-row__dot");
                face.Add(dot);
            }

            row.Add(face);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("lobby-seat__name");
            text.Add(new Label(entry.Name + (entry.Favourite ? " ★" : "")));
            var sub = new Label(detail);
            sub.AddToClassList("caption");
            text.Add(sub);
            row.Add(text);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                {
                    return;
                }

                OpenPlayerSheet(entry);
            });
            return row;
        }

        /// <summary>
        /// The player sheet (design: Player sheet): face, name and handle, where they are, one yellow
        /// action that follows their state, Favourite, and Remove and Block as quiet links.
        /// </summary>
        private void OpenPlayerSheet(FriendEntry entry)
        {
            var panel = Sheet.Open(_root, null);
            panel.AddToClassList("player-card");
            var head = new VisualElement();
            head.AddToClassList("player-card__head");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("player-card__face");
            var initial = new Label { pickingMode = PickingMode.Ignore };
            initial.AddToClassList("seat__initial");
            face.Add(initial);
            AvatarArt.Apply(face, initial, AvatarArt.ForAccount(entry.UserId), entry.Name);
            if (entry.Online)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("player-card__dot");
                face.Add(dot);
            }

            head.Add(face);
            var who = new VisualElement();
            who.AddToClassList("player-card__who");
            var name = new Label(entry.Name);
            name.AddToClassList("player-card__name");
            who.Add(name);
            // @handle · title and the stats line come from their public profile (design: Player sheet).
            var handle = new Label("");
            handle.AddToClassList("player-card__handle");
            who.Add(handle);
            var stats = new Label("");
            stats.AddToClassList("player-card__stats");
            who.Add(stats);
            FillProfile(entry, handle, stats);
            head.Add(who);
            if (entry.State == FriendState.Friend)
            {
                // The star top right: on for a favourite, a tap pins or unpins them.
                var star = new Button(() => { Sheet.Close(panel); _actions.Favourite(entry); }) { text = "", tooltip = entry.Favourite ? UiText.UnfavouriteWord : UiText.FavouriteWord };
                star.AddToClassList("player-card__star");
                star.EnableInClassList("player-card__star--on", entry.Favourite);
                head.Add(star);
            }

            panel.Add(head);
            var presence = PresenceOf?.Invoke(entry.UserId);
            var friend = entry.State == FriendState.Friend;
            string where;
            Button? main = null;
            if (presence?.TableCode is string code && presence.Joinable)
            {
                where = Strings.Format(UiText.WhereTable, presence.OpenSeats);
                main = Sheet.Button(Strings.Format(UiText.JoinTheirTable, entry.Name), () => { Sheet.Close(panel); Join?.Invoke(code); }, "yes");
            }
            else if (presence?.TableCode is string watchCode && presence.Watchable)
            {
                where = UiText.WhereWatching;
                main = Sheet.Button(UiText.Watch, () => { Sheet.Close(panel); WatchTable?.Invoke(watchCode); }, "yes");
            }
            else if (entry.State == FriendState.Received)
            {
                where = entry.Online ? UiText.Online : UiText.Offline;
                main = Sheet.Button(UiText.Accept, () => { Sheet.Close(panel); _actions.Add(entry.UserId); }, "yes");
            }
            else if (!friend && entry.State != FriendState.Sent)
            {
                where = entry.Rounds > 0 ? Strings.Get("ui.rounds_together", entry.Rounds) : "";
                main = Sheet.Button(UiText.AddFriend, () => { Sheet.Close(panel); _actions.Add(entry.UserId); }, "yes");
            }
            else
            {
                where = entry.Online ? UiText.OnHome : UiText.Offline;
            }

            if (where.Length > 0)
            {
                var line = new Label(where);
                line.AddToClassList("player-card__where");
                panel.Add(line);
            }

            if (main != null)
            {
                main.AddToClassList("player-card__main");
                panel.Add(main);
            }

            if (friend)
            {
                var row = Sheet.Answers(panel);
                if (InviteToMine != null && entry.Online)
                {
                    row.Add(Sheet.Button(UiText.InviteToMine, () => { Sheet.Close(panel); InviteToMine(entry.UserId); }));
                }

                row.Add(Sheet.Button(entry.Favourite ? UiText.UnfavouriteWord : UiText.FavouriteWord, () => { Sheet.Close(panel); _actions.Favourite(entry); }));
            }

            // Quiet links at the bottom for everyone: Remove friend for a friend, then Block.
            var links = new VisualElement();
            links.AddToClassList("player-card__links");
            if (friend)
            {
                links.Add(Link(UiText.RemoveFriend, () => { Sheet.Close(panel); _actions.Remove(entry.UserId); }));
                links.Add(Dot());
            }

            links.Add(Link(UiText.Block, () => { Sheet.Close(panel); _actions.Block(entry.UserId); }));
            panel.Add(links);
        }

        /// <summary>Looks up a player's public profile for the sheet's handle, title and stats.</summary>
        public Func<string, Awaitable<ProfileCard?>>? LookUpProfile { get; set; }

        private async void FillProfile(FriendEntry entry, Label handle, Label stats)
        {
            handle.text = entry.Handle != null ? Strings.Format(UiText.PlayerLine, entry.Handle) : "";
            var profile = LookUpProfile == null ? null : await LookUpProfile(entry.UserId);
            if (profile == null)
            {
                return;
            }

            handle.text = string.IsNullOrEmpty(profile.Title)
                ? Strings.Format(UiText.PlayerLine, profile.Handle)
                : Strings.Format(UiText.PlayerLineTitle, profile.Handle, UiText.TitleName(profile.Title!));
            stats.text = Strings.Format(UiText.PlayerStats, Strings.Get("ui.rounds_count", profile.Rounds), Strings.Get("ui.times_bhabhi", profile.Bhabhis));
        }

        private static Label Dot()
        {
            var dot = new Label(UiText.Dot) { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("player-card__sep");
            return dot;
        }

        private static Button Link(string text, Action click)
        {
            var link = new Button(click) { text = text };
            link.AddToClassList("player-card__link");
            return link;
        }

        /// <summary>The add-friend sheet (design: Add friend): your code with Share, their code and Add.</summary>
        private void OpenAddSheet()
        {
            var panel = Sheet.Open(_root, UiText.AddFriendTitle);
            panel.AddToClassList("add-friend");
            var mine = MyCode?.Invoke();
            if (!string.IsNullOrEmpty(mine))
            {
                panel.Add(Sheet.Line(UiText.YourFriendCode, "add-friend__label"));
                var codeRow = new VisualElement();
                codeRow.AddToClassList("add-friend__mine");
                var code = new Label(mine);
                code.AddToClassList("add-friend__code");
                codeRow.Add(code);
                if (_showMyCode != null)
                {
                    var share = Sheet.Button(UiText.Share, _showMyCode, "teal");
                    share.AddToClassList("add-friend__share");
                    codeRow.Add(share);
                }

                panel.Add(codeRow);
            }

            panel.Add(Sheet.Line(UiText.TheirCode, "add-friend__label"));
            var field = new TextField { maxLength = 10, name = "code" };
            field.AddToClassList("add-friend__field");
            panel.Add(field);
            var result = Sheet.Line("", "add-friend__result");
            var add = Sheet.Button(UiText.Add, () =>
            {
                if (field.value.Trim().Length == 0)
                {
                    return;
                }

                _actions.AddByCode(field.value.Trim());
                result.text = UiText.RequestSent;
                field.value = "";
            }, "yes");
            add.AddToClassList("add-friend__add");
            panel.Add(add);
            panel.Add(result);
            field.schedule.Execute(() => field.Focus()).StartingIn(50);
        }

        private static Button Primary(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            button.AddToClassList("chip--on");
            return button;
        }

        private static Button Quiet(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            return button;
        }

        private static Button Teal(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            button.AddToClassList("chip--teal");
            return button;
        }

        private static Button Dismiss(Action click)
        {
            var button = new Button(click) { text = "", tooltip = UiText.Decline };
            button.AddToClassList("friend-row__dismiss");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("kit__icon");
            icon.AddToClassList("kit__icon--close");
            button.Add(icon);
            return button;
        }

        /// <summary>What the screen's buttons do; the owning flow runs them against the server.</summary>
        public sealed class Actions
        {
            public Actions(Action<string> addByCode, Action<string> add, Action<string> remove, Action<string> block, Action<FriendEntry> favourite, Action<string>? unblock = null)
            {
                Unblock = unblock ?? remove;
                AddByCode = addByCode;
                Add = add;
                Remove = remove;
                Block = block;
                Favourite = favourite;
            }

            public Action<string> AddByCode { get; }

            public Action<string> Add { get; }

            public Action<string> Remove { get; }

            public Action<string> Block { get; }

            public Action<FriendEntry> Favourite { get; }

            public Action<string> Unblock { get; }
        }
    }
}
