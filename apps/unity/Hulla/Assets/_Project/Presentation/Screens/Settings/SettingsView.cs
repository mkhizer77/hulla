using System;
using System.Collections.Generic;
using Hulla.Application.Settings;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Settings
{
    /// <summary>The settings that exist so far (GDD 14.12, 27); every change saves and applies at once.</summary>
    public sealed class SettingsView : IDisposable
    {
        private readonly SettingsModel _settings;
        private readonly VisualElement _list;

        /// <summary>What the account rows do; null hides the section (not signed in).</summary>
        public sealed class AccountActions
        {
            public AccountActions(Action<Action<string>> export, Action<Action<string>> delete, Action<Action<string>> cancelDelete, Func<string?> pendingText, Action? phone = null)
            {
                Phone = phone;
                Export = export;
                Delete = delete;
                CancelDelete = cancelDelete;
                PendingText = pendingText;
            }

            /// <summary>Runs the action; the callback gets a one-line status to show.</summary>
            /// <summary>Opens the phone number screen (GDD 10.2); null hides the row.</summary>
            public Action? Phone { get; }

            public Action<Action<string>> Export { get; }

            public Action<Action<string>> Delete { get; }

            public Action<Action<string>> CancelDelete { get; }

            /// <summary>"Your account will be deleted on …" while a deletion is pending, else null.</summary>
            public Func<string?> PendingText { get; }
        }

        private readonly AccountActions? _account;
        private Label? _accountStatus;
        private readonly NotificationSettingsViewModel? _notifications;
        private readonly Action<Action<NotificationSettings>>? _changeNotifications;
        private readonly string? _server;
        private readonly Action<string>? _useServer;

        /// <param name="notifications">The server-side notification switches; null hides the section.</param>
        /// <param name="server">The server address, shown in development builds only; null hides it.</param>
        /// <param name="useServer">Switches to the address typed in, when the field is left or submitted.</param>
        public SettingsView(VisualElement root, SettingsModel settings, Action close, AccountActions? account = null, NotificationSettingsViewModel? notifications = null, Action<Action<NotificationSettings>>? changeNotifications = null, string? server = null, Action<string>? useServer = null)
        {
            _server = server;
            _useServer = useServer;
            _account = account;
            _notifications = notifications;
            _changeNotifications = changeNotifications;
            if (_notifications != null)
            {
                _notifications.Changed += Rebuild;
            }

            _settings = settings;
            root.Q<Label>("title").text = UiText.Settings;
            var closeButton = root.Q<Button>("close");
            closeButton.text = UiText.Done;
            closeButton.clicked += close;
            _list = root.Q<VisualElement>("list");
            Build();
        }

        public void Dispose()
        {
            if (_notifications != null)
            {
                _notifications.Changed -= Rebuild;
            }
        }

        private void Rebuild()
        {
            var status = _accountStatus?.text;
            _list.Clear();
            Build();
            if (_accountStatus != null && status != null)
            {
                _accountStatus.text = status;
            }
        }

        private void Build()
        {
            Heading(UiText.SettingsPlay);
            Toggle(UiText.OneTapPlay, () => _settings.OneTapPlay, v => _settings.OneTapPlay = v);
            Choice(UiText.HandSortLabel, new[] { UiText.SortBySuit, UiText.SortByRank }, () => (int)_settings.HandSort, i => _settings.HandSort = (HandSort)i);
            Toggle(UiText.RulesRecapLabel, () => _settings.ShowRulesRecap, v => _settings.ShowRulesRecap = v);
            Heading(UiText.SettingsSound);
            Level(UiText.SfxLabel, () => _settings.SfxVolume, v => _settings.SfxVolume = v);
            Toggle(UiText.VoiceChatLabel, () => _settings.VoiceChat, v => _settings.VoiceChat = v);
            Level(UiText.VoiceVolumeLabel, () => _settings.VoiceVolume, v => _settings.VoiceVolume = v);
            Toggle(UiText.HapticsLabel, () => _settings.Haptics, v => _settings.Haptics = v);
            Heading(UiText.SettingsSeeing);
            Toggle(UiText.FourColourLabel, () => _settings.FourColourDeck, v => _settings.FourColourDeck = v);
            Toggle(UiText.HighContrastLabel, () => _settings.HighContrast, v => _settings.HighContrast = v);
            Toggle(UiText.LargeCardsLabel, () => _settings.LargeCards, v => _settings.LargeCards = v);
            var scales = new[] { TextScale.Normal, TextScale.Large, TextScale.Larger, TextScale.Largest };
            Choice(UiText.TextSizeLabel, new[] { "100%", "125%", "150%", "200%" }, () => Array.IndexOf(scales, _settings.TextScale), i => _settings.TextScale = scales[i]);
            Toggle(UiText.ReduceMotionLabel, () => _settings.ReduceMotion, v => _settings.ReduceMotion = v);
            Toggle(UiText.LeftHandedLabel, () => _settings.LeftHanded, v => _settings.LeftHanded = v);
            var push = _notifications?.Settings;
            if (push != null && _changeNotifications != null)
            {
                Heading(UiText.Notifications);
                var change = _changeNotifications;
                Toggle(UiText.NotifyInvites, () => push.TableInvite, v => change(n => n.TableInvite = v));
                Toggle(UiText.NotifyFriendRequests, () => push.FriendRequest, v => change(n => n.FriendRequest = v));
                Toggle(UiText.NotifyFriendOnline, () => push.FriendOnline, v => change(n => n.FriendOnline = v));
                Toggle(UiText.NotifyDailyReward, () => push.DailyReward, v => change(n => n.DailyReward = v));
                Toggle(UiText.NotifySeasonEnd, () => push.SeasonEnd, v => change(n => n.SeasonEnd = v));
                Toggle(UiText.NotifyQuietHours, () => push.QuietHours, v => change(n => n.QuietHours = v));
                Toggle(UiText.NotifyNonFriendInvites, () => push.InvitesFromNonFriends, v => change(n => n.InvitesFromNonFriends = v));
            }

            Heading(UiText.SettingsPrivacy);
            Toggle(UiText.AnalyticsLabel, () => _settings.AnalyticsConsent == true, v => _settings.AnalyticsConsent = v);
            if (_account != null)
            {
                Heading(UiText.SettingsAccount);
                if (_account.Phone != null)
                {
                    Action(Strings.Text("phone.settings_action"), _account.Phone);
                }

                Action(UiText.ExportData, () => _account.Export(Status));
                var pending = _account.PendingText();
                if (pending == null)
                {
                    Action(UiText.DeleteAccount, () => _account.Delete(Status)).AddToClassList("button--danger");
                }
                else
                {
                    Action(UiText.CancelDeletion, () => _account.CancelDelete(Status));
                }

                _accountStatus = new Label(pending ?? "");
                _accountStatus.AddToClassList("status");
                _list.Add(_accountStatus);
            }

            if (_server != null && _useServer != null)
            {
                Heading(UiText.SettingsDevelopment);
                var field = new TextField(UiText.Server) { name = "server", value = _server };
                field.AddToClassList("field");
                field.AddToClassList("field--dev");
                var use = _useServer;
                field.RegisterCallback<FocusOutEvent>(_ => use(field.value.Trim()));
                field.RegisterCallback<NavigationSubmitEvent>(_ => use(field.value.Trim()));
                _list.Add(field);
            }
        }

        private void Status(string text)
        {
            _list.Clear();
            Build();
            if (_accountStatus != null)
            {
                _accountStatus.text = text;
            }
        }

        private Button Action(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("button");
            button.AddToClassList("settings__action");
            _list.Add(button);
            return button;
        }

        private void Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("caption");
            label.AddToClassList("settings__heading");
            _list.Add(label);
        }

        private void Toggle(string text, Func<bool> get, Action<bool> set)
        {
            var toggle = new Toggle(text) { value = get() };
            toggle.AddToClassList("settings__row");
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
            _list.Add(toggle);
        }

        /// <summary>A 0 to 100 level (GDD 16.5) as a slider; saved when the finger lifts.</summary>
        private void Level(string text, Func<int> get, Action<int> set)
        {
            var slider = new SliderInt(text, 0, 100) { value = get() };
            slider.AddToClassList("settings__row");
            slider.AddToClassList("settings__level");
            var value = new Label(get() + "%") { pickingMode = PickingMode.Ignore };
            value.AddToClassList("settings__value");
            slider.Add(value);
            slider.RegisterValueChangedCallback(evt =>
            {
                value.text = evt.newValue + "%";
                set(evt.newValue);
            });
            _list.Add(slider);
        }

        private void Choice(string text, string[] options, Func<int> get, Action<int> set)
        {
            var row = new VisualElement();
            row.AddToClassList("settings__row");
            row.AddToClassList("settings__choice");
            row.Add(new Label(text));
            var chips = new VisualElement();
            chips.AddToClassList("chips");
            var buttons = new Button[options.Length];
            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var chip = new Button { text = options[i] };
                chip.AddToClassList("chip");
                chip.clicked += () =>
                {
                    set(index);
                    for (var j = 0; j < buttons.Length; j++)
                    {
                        buttons[j].EnableInClassList("chip--on", j == index);
                    }
                };
                chip.EnableInClassList("chip--on", i == get());
                buttons[i] = chip;
                chips.Add(chip);
            }

            row.Add(chips);
            _list.Add(row);
        }
    }
}
