using System;
using System.Collections.Generic;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Friends
{
    /// <summary>
    /// Add by code, requests in and out, friends (favourites first) with online state, recent
    /// co-players to add, and blocks (GDD 10.3).
    /// </summary>
    public sealed class FriendsView : IDisposable
    {
        private readonly FriendsViewModel _model;
        private readonly VisualElement _list;
        private readonly Label _status;
        private readonly TextField _code;
        private readonly Actions _actions;

        private readonly Action? _retry;
        private readonly Action? _showMyCode;

        /// <param name="retry">Loads the lists again after an error; null hides Retry.</param>
        /// <param name="showMyCode">Opens the profile with the friend code from the empty state.</param>
        public FriendsView(VisualElement root, FriendsViewModel model, Action back, Actions actions, Action? retry = null, Action? showMyCode = null)
        {
            _model = model;
            _actions = actions;
            _retry = retry;
            _showMyCode = showMyCode;
            root.Q<Label>("title").text = UiText.Friends;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _code = root.Q<TextField>("code");
            _code.label = UiText.AddByCode;
            _code.maxLength = 10;
            var add = root.Q<Button>("add");
            add.text = UiText.Add;
            add.clicked += () => actions.AddByCode(_code.value);
            _status = root.Q<Label>("status");
            _list = root.Q<VisualElement>("list");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _status.text = _model.RequestSent ? UiText.RequestSent : "";
            if (_model.RequestSent)
            {
                _code.value = "";
            }

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

            Section(UiText.Requests, _model.Received, entry => new[]
            {
                Action(UiText.Accept, () => _actions.Add(entry.UserId), true),
                Action(UiText.Decline, () => _actions.Remove(entry.UserId), danger: true),
            });
            Section(UiText.FriendsList, _model.Friends, entry => new[]
            {
                Action(entry.Favourite ? UiText.Favourite : UiText.NotFavourite, () => _actions.Favourite(entry)),
                Action(UiText.Remove, () => _actions.Remove(entry.UserId), danger: true),
                Action(UiText.Block, () => _actions.Block(entry.UserId), danger: true),
            }, UiText.NoFriendsYet);
            Section(UiText.SentRequests, _model.Sent, entry => new[] { Action(UiText.CancelRequest, () => _actions.Remove(entry.UserId)) });
            Section(UiText.Recent, _model.Recent, entry => new[] { Action(UiText.AddFriend, () => _actions.Add(entry.UserId), true) });
            Section(UiText.BlockedList, _model.Blocked, entry => new[] { Action(UiText.Unblock, () => _actions.Unblock(entry.UserId)) });
        }

        private void Section(string title, IReadOnlyList<FriendEntry> entries, Func<FriendEntry, Button[]> buttons, string? empty = null)
        {
            if (entries.Count == 0 && empty == null)
            {
                return;
            }

            var heading = new Label(title);
            heading.AddToClassList("caption");
            heading.AddToClassList("settings__heading");
            _list.Add(heading);
            if (entries.Count == 0)
            {
                ListState.Empty(_list, empty!, _showMyCode == null ? null : UiText.ShowMyCode, _showMyCode);
                return;
            }

            foreach (var entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                row.AddToClassList("friend-row");
                var text = new VisualElement();
                text.AddToClassList("lobby-seat__name");
                text.Add(new Label(entry.Name));
                var detail = entry.Rounds > 0 && entry.State == null
                    ? Strings.Format(UiText.RoundsTogether, entry.Rounds)
                    : entry.State == FriendState.Friend ? (entry.Online ? UiText.Online : UiText.Offline) : entry.Handle != null ? "@" + entry.Handle : "";
                var sub = new Label(detail);
                sub.AddToClassList("caption");
                text.Add(sub);
                row.Add(text);
                foreach (var button in buttons(entry))
                {
                    row.Add(button);
                }

                _list.Add(row);
            }
        }

        private static Button Action(string text, Action click, bool primary = false, bool danger = false)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            if (primary)
            {
                button.AddToClassList("chip--on");
            }

            button.EnableInClassList("button--danger", danger);

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
