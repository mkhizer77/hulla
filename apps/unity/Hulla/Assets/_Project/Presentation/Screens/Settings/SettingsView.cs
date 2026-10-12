using System;
using Hulla.Application.Settings;
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
            public AccountActions(Action<Action<string>> export, Action<Action<string>> delete, Action<Action<string>> cancelDelete, Func<string?> pendingText)
            {
                Export = export;
                Delete = delete;
                CancelDelete = cancelDelete;
                PendingText = pendingText;
            }

            /// <summary>Runs the action; the callback gets a one-line status to show.</summary>
            public Action<Action<string>> Export { get; }

            public Action<Action<string>> Delete { get; }

            public Action<Action<string>> CancelDelete { get; }

            /// <summary>"Your account will be deleted on …" while a deletion is pending, else null.</summary>
            public Func<string?> PendingText { get; }
        }

        private readonly AccountActions? _account;
        private Label? _accountStatus;

        public SettingsView(VisualElement root, SettingsModel settings, Action close, AccountActions? account = null)
        {
            _account = account;
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
        }

        private void Build()
        {
            Heading(UiText.SettingsPlay);
            Toggle(UiText.OneTapPlay, () => _settings.OneTapPlay, v => _settings.OneTapPlay = v);
            Choice(UiText.HandSortLabel, new[] { UiText.SortBySuit, UiText.SortByRank }, () => (int)_settings.HandSort, i => _settings.HandSort = (HandSort)i);
            Toggle(UiText.HapticsLabel, () => _settings.Haptics, v => _settings.Haptics = v);
            Toggle(UiText.RulesRecapLabel, () => _settings.ShowRulesRecap, v => _settings.ShowRulesRecap = v);
            Heading(UiText.SettingsSeeing);
            Toggle(UiText.FourColourLabel, () => _settings.FourColourDeck, v => _settings.FourColourDeck = v);
            Toggle(UiText.HighContrastLabel, () => _settings.HighContrast, v => _settings.HighContrast = v);
            Toggle(UiText.LargeCardsLabel, () => _settings.LargeCards, v => _settings.LargeCards = v);
            var scales = new[] { TextScale.Normal, TextScale.Large, TextScale.Larger, TextScale.Largest };
            Choice(UiText.TextSizeLabel, new[] { "100%", "125%", "150%", "200%" }, () => Array.IndexOf(scales, _settings.TextScale), i => _settings.TextScale = scales[i]);
            Toggle(UiText.ReduceMotionLabel, () => _settings.ReduceMotion, v => _settings.ReduceMotion = v);
            Toggle(UiText.LeftHandedLabel, () => _settings.LeftHanded, v => _settings.LeftHanded = v);
            Heading(UiText.SettingsPrivacy);
            Toggle(UiText.AnalyticsLabel, () => _settings.AnalyticsConsent == true, v => _settings.AnalyticsConsent = v);
            if (_account != null)
            {
                Heading(UiText.SettingsAccount);
                Action(UiText.ExportData, () => _account.Export(Status));
                var pending = _account.PendingText();
                if (pending == null)
                {
                    Action(UiText.DeleteAccount, () => _account.Delete(Status));
                }
                else
                {
                    Action(UiText.CancelDeletion, () => _account.CancelDelete(Status));
                }

                _accountStatus = new Label(pending ?? "");
                _accountStatus.AddToClassList("status");
                _list.Add(_accountStatus);
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

        private void Action(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("button");
            button.AddToClassList("settings__action");
            _list.Add(button);
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
