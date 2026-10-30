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
        private readonly Label _title;
        private readonly Button _closeButton;

        /// <summary>The pages behind the rows on Settings (design: Settings pages).</summary>
        public enum Page
        {
            Main,
            Notifications,
            Privacy,
            Account,
            Help,
            TextSize,
            Language,
        }

        private Page _page = Page.Main;

        /// <summary>Opens the tutorial and the rulebook from Help; null hides the rows.</summary>
        public Action? HowToPlay { get; set; }

        public Action? Rulebook { get; set; }

        /// <summary>Blocked players and Unblock on the Privacy page; null hides the list.</summary>
        public Func<IReadOnlyList<FriendEntry>>? Blocked { get; set; }

        public Action<string>? Unblock { get; set; }

        /// <summary>Shows one page of Settings; the main page has the switches and the rows to the rest.</summary>
        public void ShowPage(Page page)
        {
            _page = page;
            _title.text = page switch
            {
                Page.Notifications => UiText.Notifications,
                Page.Privacy => UiText.PrivacyBlocked,
                Page.Account => UiText.AccountData,
                Page.Help => UiText.HelpHow,
                Page.TextSize => UiText.TextSizeLabel,
                Page.Language => UiText.Language,
                _ => UiText.Settings,
            };
            _closeButton.style.display = page == Page.Main ? DisplayStyle.Flex : DisplayStyle.None;
            Rebuild();
        }

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
            _title = root.Q<Label>("title");
            _title.text = UiText.Settings;
            _closeButton = root.Q<Button>("close");
            _closeButton.text = UiText.Done;
            _closeButton.clicked += close;
            var backLeft = root.Q<Button>("back-left");
            if (backLeft != null)
            {
                backLeft.text = "";
                backLeft.tooltip = UiText.Back;
                backLeft.clicked += () =>
                {
                    if (_page == Page.Main)
                    {
                        close();
                    }
                    else
                    {
                        ShowPage(_page == Page.TextSize || _page == Page.Language ? Page.Main : Page.Main);
                    }
                };
            }

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
            _group = null;
            Build();
            if (_accountStatus != null && status != null)
            {
                _accountStatus.text = status;
            }
        }

        private void Build()
        {
            switch (_page)
            {
                case Page.Notifications:
                    BuildNotifications();
                    return;
                case Page.Privacy:
                    BuildPrivacy();
                    return;
                case Page.Account:
                    BuildAccount();
                    return;
                case Page.Help:
                    Heading(null);
                    if (HowToPlay != null)
                    {
                        Row(UiText.HowToPlayRow, "", HowToPlay);
                    }

                    if (Rulebook != null)
                    {
                        Row(UiText.RulebookRow, "", Rulebook);
                    }

                    return;
                case Page.TextSize:
                    Heading(null);
                    var scales = new[] { TextScale.Normal, TextScale.Large, TextScale.Larger, TextScale.Largest };
                    var labels = new[] { "100%", "125%", "150%", "200%" };
                    for (var i = 0; i < scales.Length; i++)
                    {
                        var scale = scales[i];
                        Pick(labels[i], _settings.TextScale == scale, () =>
                        {
                            _settings.TextScale = scale;
                            Rebuild();
                        });
                    }

                    return;
                case Page.Language:
                    Heading(null);
                    Pick(LanguageName(), true, () => { });
                    return;
            }

            // The main page (design: Settings): three groups of switches, then the rows to the pages.
            Heading(UiText.SettingsPlay);
            Toggle(UiText.OneTapPlay, () => _settings.OneTapPlay, v => _settings.OneTapPlay = v);
            Segment(UiText.HandSortLabel, new[] { UiText.SuitShort, UiText.RankShort }, () => (int)_settings.HandSort, i => _settings.HandSort = (HandSort)i);
            Toggle(UiText.RulesRecapLabel, () => _settings.ShowRulesRecap, v => _settings.ShowRulesRecap = v);
            Heading(UiText.SettingsSound);
            Level(UiText.SfxLabel, () => _settings.SfxVolume, v => _settings.SfxVolume = v);
            Toggle(UiText.HapticsLabel, () => _settings.Haptics, v => _settings.Haptics = v);
            Toggle(UiText.VoiceChatLabel, () => _settings.VoiceChat, v => _settings.VoiceChat = v);
            Level(UiText.VoiceVolumeLabel, () => _settings.VoiceVolume, v => _settings.VoiceVolume = v);
            Heading(UiText.SettingsSeeing);
            Toggle(UiText.FourColourLabel, () => _settings.FourColourDeck, v => _settings.FourColourDeck = v);
            Toggle(UiText.HighContrastLabel, () => _settings.HighContrast, v => _settings.HighContrast = v);
            Toggle(UiText.LargeCardsLabel, () => _settings.LargeCards, v => _settings.LargeCards = v);
            var percent = _settings.TextScale switch { TextScale.Large => "125%", TextScale.Larger => "150%", TextScale.Largest => "200%", _ => "100%" };
            Row(UiText.TextSizeLabel, percent, () => ShowPage(Page.TextSize));
            Toggle(UiText.ReduceMotionLabel, () => _settings.ReduceMotion, v => _settings.ReduceMotion = v);
            Toggle(UiText.LeftHandedLabel, () => _settings.LeftHanded, v => _settings.LeftHanded = v);
            Heading(UiText.SettingsMore);
            if (_notifications?.Settings != null && _changeNotifications != null)
            {
                Row(UiText.Notifications, UiText.NotifySummary, () => ShowPage(Page.Notifications));
            }

            Row(UiText.PrivacyBlocked, "", () => ShowPage(Page.Privacy));
            if (_account != null)
            {
                Row(UiText.AccountData, _account.PendingText() ?? "", () => ShowPage(Page.Account));
            }

            if (HowToPlay != null || Rulebook != null)
            {
                Row(UiText.HelpHow, "", () => ShowPage(Page.Help));
            }

            var footer = new Button(() => ShowPage(Page.Language)) { text = Strings.Format(UiText.VersionFooter, UnityEngine.Application.version, LanguageName()) };
            footer.AddToClassList("settings__footer");
            _list.Add(footer);
            if (_server != null && _useServer != null)
            {
                Heading(UiText.SettingsDevelopment);
                var field = new TextField(UiText.Server) { name = "server", value = _server };
                field.AddToClassList("field");
                field.AddToClassList("field--dev");
                var use = _useServer;
                field.RegisterCallback<FocusOutEvent>(_ => use(field.value.Trim()));
                field.RegisterCallback<NavigationSubmitEvent>(_ => use(field.value.Trim()));
                Group.Add(field);
            }
        }

        private void BuildNotifications()
        {
            var push = _notifications?.Settings;
            if (push == null || _changeNotifications == null)
            {
                return;
            }

            Heading(null);
            var change = _changeNotifications;
            Toggle(UiText.NotifyInvites, () => push.TableInvite, v => change(n => n.TableInvite = v));
            Toggle(UiText.NotifyFriendRequests, () => push.FriendRequest, v => change(n => n.FriendRequest = v));
            Toggle(UiText.NotifyFriendOnline, () => push.FriendOnline, v => change(n => n.FriendOnline = v));
            Toggle(UiText.NotifyDailyReward, () => push.DailyReward, v => change(n => n.DailyReward = v));
            Toggle(UiText.NotifySeasonEnd, () => push.SeasonEnd, v => change(n => n.SeasonEnd = v));
            Heading(null);
            Toggle(UiText.NotifyQuietHours, () => push.QuietHours, v => change(n => n.QuietHours = v));
            Toggle(UiText.NotifyNonFriendInvites, () => push.InvitesFromNonFriends, v => change(n => n.InvitesFromNonFriends = v));
        }

        private void BuildPrivacy()
        {
            Heading(null);
            Toggle(UiText.AnalyticsLabel, () => _settings.AnalyticsConsent == true, v => _settings.AnalyticsConsent = v);
            if (Blocked == null)
            {
                return;
            }

            Heading(UiText.BlockedPlayers);
            var blocked = Blocked();
            if (blocked.Count == 0)
            {
                var none = new Label(UiText.NoBlocked);
                none.AddToClassList("settings__note");
                Group.Add(none);
                return;
            }

            foreach (var player in blocked)
            {
                var id = player.UserId;
                var row = new VisualElement();
                row.AddToClassList("settings__row");
                row.AddToClassList("settings__link-row");
                row.Add(new Label(player.Name));
                var unblock = new Button(() =>
                {
                    Unblock?.Invoke(id);
                    row.RemoveFromHierarchy();
                }) { text = UiText.Unblock };
                unblock.AddToClassList("chip");
                row.Add(unblock);
                Group.Add(row);
            }
        }

        private void BuildAccount()
        {
            if (_account == null)
            {
                return;
            }

            Heading(null);
            if (_account.Phone != null)
            {
                Row(Strings.Text("phone.settings_action"), "", _account.Phone);
            }

            Row(UiText.ExportData, "", () => _account.Export(Status));
            var pending = _account.PendingText();
            if (pending == null)
            {
                Row(UiText.DeleteAccount, "", () => _account.Delete(Status)).AddToClassList("settings__link-row--danger");
            }
            else
            {
                Row(UiText.CancelDeletion, "", () => _account.CancelDelete(Status));
            }

            _accountStatus = new Label(pending ?? "");
            _accountStatus.AddToClassList("status");
            Group.Add(_accountStatus);
        }

        private static string LanguageName()
        {
            var culture = Strings.Culture;
            var name = culture.NativeName;
            var paren = name.IndexOf(" (", StringComparison.Ordinal);
            name = paren > 0 ? name.Substring(0, paren) : name;
            return name.Length > 0 ? char.ToUpper(name[0], culture) + name.Substring(1) : name;
        }

        /// <summary>A row that opens a page or runs an action: label left, value and › right.</summary>
        private VisualElement Row(string text, string value, Action open)
        {
            var row = new Button(open) { text = "" };
            row.AddToClassList("settings__row");
            row.AddToClassList("settings__link-row");
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("settings__link-label");
            row.Add(label);
            var right = new Label(value.Length > 0 ? value + "  ›" : "›") { pickingMode = PickingMode.Ignore };
            right.AddToClassList("settings__link-value");
            row.Add(right);
            Group.Add(row);
            return row;
        }

        /// <summary>One choice in a list page, with a tick on the current one.</summary>
        private void Pick(string text, bool chosen, Action pick)
        {
            var row = new Button(pick) { text = "" };
            row.AddToClassList("settings__row");
            row.AddToClassList("settings__link-row");
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("settings__link-label");
            row.Add(label);
            var tick = new Label(chosen ? "✓" : "") { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("settings__link-value");
            tick.AddToClassList("settings__tick");
            row.Add(tick);
            Group.Add(row);
        }

        /// <summary>Two short options as a small pill on the right of the row (Suit | Rank).</summary>
        private void Segment(string text, string[] options, Func<int> get, Action<int> set)
        {
            var row = new VisualElement();
            row.AddToClassList("settings__row");
            row.AddToClassList("settings__seg-row");
            var label = new Label(text);
            label.AddToClassList("settings__link-label");
            row.Add(label);
            var pill = new VisualElement();
            pill.AddToClassList("mini-seg");
            pill.AddToClassList("settings__seg");
            var buttons = new Button[options.Length];
            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var item = new Button { text = options[i] };
                item.AddToClassList("mini-seg__item");
                item.clicked += () =>
                {
                    set(index);
                    for (var j = 0; j < buttons.Length; j++)
                    {
                        buttons[j].EnableInClassList("mini-seg__item--on", j == index);
                    }
                };
                item.EnableInClassList("mini-seg__item--on", i == get());
                buttons[i] = item;
                pill.Add(item);
            }

            row.Add(pill);
            Group.Add(row);
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

        private VisualElement? _group;

        /// <summary>A heading opens a new card (docs/design/basant-tabs.md); the rows under it sit inside.</summary>
        private void Heading(string? text)
        {
            if (text != null)
            {
                var label = new Label(text);
                label.AddToClassList("caption");
                label.AddToClassList("settings__heading");
                _list.Add(label);
            }

            _group = new VisualElement();
            _group.AddToClassList("settings__group");
            _list.Add(_group);
        }

        private VisualElement Group => _group ?? _list;

        private void Toggle(string text, Func<bool> get, Action<bool> set)
        {
            var toggle = new Toggle(text) { value = get() };
            toggle.AddToClassList("settings__row");
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
            Group.Add(toggle);
        }

        /// <summary>A 0 to 100 level (GDD 16.5) as a slider; saved when the finger lifts.</summary>
        private void Level(string text, Func<int> get, Action<int> set)
        {
            var slider = new SliderInt(text, 0, 100) { value = get() };
            slider.AddToClassList("settings__row");
            slider.AddToClassList("settings__level");
            slider.RegisterValueChangedCallback(evt => set(evt.newValue));
            Group.Add(slider);
        }
    }
}
