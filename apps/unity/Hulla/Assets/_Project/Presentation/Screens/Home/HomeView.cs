using System;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>Name, create a table, or join one by code.</summary>
    public sealed class HomeView : IDisposable
    {
        private readonly HomeViewModel _model;
        private readonly TextField _name;
        private readonly TextField _code;
        private readonly Button _create;
        private readonly Button _join;
        private readonly Label _status;
        private readonly TextField _server;

        /// <param name="server">The server to show in a development build, or null to hide the field.</param>
        private readonly Button _quick;
        private int _seats = 4;

        public HomeView(VisualElement root, HomeViewModel model, string? server, Action<string, string?> create, Action<string, string, string?> join, Action<string, int, string?> quickPlay, Action? openSettings = null)
        {
            var settings = root.Q<Button>("settings");
            settings.text = UiText.Settings;
            settings.clicked += () => openSettings?.Invoke();
            _model = model;
            root.Q<Label>("title").text = UiText.AppTitle;
            root.Q<Label>("tagline").text = UiText.Tagline;
            _name = root.Q<TextField>("name");
            _name.label = UiText.NameLabel;
            _name.value = model.LastName;
            _name.maxLength = 16;
            _code = root.Q<TextField>("code");
            _code.label = UiText.CodeLabel;
            _code.maxLength = 8;
            _create = root.Q<Button>("create");
            _create.text = UiText.CreateTable;
            _join = root.Q<Button>("join");
            _join.text = UiText.JoinTable;
            _status = root.Q<Label>("status");
            _server = root.Q<TextField>("server");
            _server.label = UiText.Server;
            _server.value = server ?? "";
            _server.style.display = server == null ? DisplayStyle.None : DisplayStyle.Flex;
            string? Server() => server == null ? null : _server.value;
            _create.clicked += () => create(_name.value, Server());
            _join.clicked += () => join(_name.value, _code.value, Server());
            root.Q<Label>("seats-label").text = UiText.SeatsLabel;
            var seatChips = root.Q<VisualElement>("seats");
            for (var n = 4; n <= 6; n++)
            {
                var count = n;
                var chip = new Button { text = count.ToString() };
                chip.AddToClassList("chip");
                chip.AddToClassList("chip--seat");
                chip.clicked += () =>
                {
                    _seats = count;
                    foreach (var other in seatChips.Children())
                    {
                        other.EnableInClassList("chip--on", other == chip);
                    }
                };
                chip.EnableInClassList("chip--on", count == _seats);
                seatChips.Add(chip);
            }

            _quick = root.Q<Button>("quick");
            _quick.text = UiText.QuickPlay;
            _quick.clicked += () => quickPlay(_name.value, _seats, Server());
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _create.SetEnabled(!_model.Busy);
            _join.SetEnabled(!_model.Busy);
            _quick.SetEnabled(!_model.Busy);
            _status.text = _model.Busy ? UiText.Connecting : _model.ErrorCode == null ? "" : UiText.Error(_model.ErrorCode);
        }
    }
}
