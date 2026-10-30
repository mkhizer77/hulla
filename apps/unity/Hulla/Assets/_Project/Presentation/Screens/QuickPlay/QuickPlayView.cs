using System;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.QuickPlay
{
    /// <summary>
    /// The search (docs/design/basant-play.md): a seat for every player the table is for, the table
    /// size asked for, the seconds so far against the usual wait, the seat size chosen, what the
    /// search is doing now, and the cross to cancel. Drawn on a 1080 x 1936 stage.
    /// </summary>
    public sealed class QuickPlayView : IDisposable
    {
        public const float StageHeight = 1936f;

        private readonly QuickPlayViewModel _model;
        private readonly Label _title;
        private readonly Label _elapsed;
        private readonly Label _status;
        private readonly VisualElement _faces;
        private readonly VisualElement _seats;
        private readonly VisualElement _fill;
        private int _shownSeconds = -1;
        private int _shownSeats = -1;
        private readonly string? _myAvatar;
        private readonly string _myName;

        public QuickPlayView(VisualElement root, QuickPlayViewModel model, Action cancel, string? myAvatar = null, string myName = "")
        {
            _model = model;
            _myAvatar = myAvatar;
            _myName = myName;
            Stage.Fit(root.Q<VisualElement>("stage-holder"), root.Q<VisualElement>("stage"), Stage.Width, StageHeight);
            _title = root.Q<Label>("title");
            _elapsed = root.Q<Label>("elapsed");
            _status = root.Q<Label>("status");
            _faces = root.Q<VisualElement>("faces");
            _seats = root.Q<VisualElement>("seats");
            _fill = root.Q<VisualElement>("bar-fill");
            root.Q<Label>("seats-label").text = UiText.SeatsShort;
            var cancelButton = root.Q<Button>("cancel");
            cancelButton.text = "";
            cancelButton.tooltip = UiText.Cancel;
            cancelButton.clicked += cancel;
            for (var n = 3; n <= 6; n++)
            {
                var medal = new VisualElement { name = "seat-" + n, pickingMode = PickingMode.Ignore };
                medal.AddToClassList("kit__medal");
                medal.AddToClassList("kit__medal--" + n);
                medal.Add(TextArt.Mark(new Label(Strings.Format("{0}", n)) { pickingMode = PickingMode.Ignore }, "digit-" + n));
                var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                ring.AddToClassList("kit__medal-ring");
                medal.Add(ring);
                _seats.Add(medal);
            }

            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _title.text = _model.Ranked ? RankedText.Searching : Strings.Format(UiText.Searching, _model.Seats);
            var seconds = (int)_model.Elapsed;
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _elapsed.text = Strings.Format(UiText.SearchElapsed, seconds);
            }

            _fill.style.width = Length.Percent(Math.Min(100f, 100f * _model.Elapsed / QuickPlayViewModel.StartAtSeconds));
            if (_model.Seats != _shownSeats)
            {
                _shownSeats = _model.Seats;
                _faces.Clear();
                for (var i = 0; i < _model.Seats; i++)
                {
                    var seat = new VisualElement { pickingMode = PickingMode.Ignore };
                    seat.AddToClassList("quick__seat");
                    if (i == 0 && _myAvatar != null)
                    {
                        // You already hold the first seat: your face in its ring, the rest dashed until found.
                        seat.AddToClassList("quick__seat--taken");
                        var face = new VisualElement { pickingMode = PickingMode.Ignore };
                        face.AddToClassList("quick__face");
                        var initial = new Label { pickingMode = PickingMode.Ignore };
                        initial.AddToClassList("seat__initial");
                        face.Add(initial);
                        TableKit.AvatarArt.Apply(face, initial, _myAvatar, _myName.Length == 0 ? "?" : _myName);
                        seat.Add(face);
                    }

                    _faces.Add(seat);
                }

                foreach (var medal in _seats.Children())
                {
                    medal.EnableInClassList("kit__medal--on", medal.name == "seat-" + _model.Seats);
                }
            }

            _seats.style.display = _model.Ranked ? DisplayStyle.None : DisplayStyle.Flex;
            switch (_model.Current)
            {
                case QuickPlayViewModel.Stage.Widened:
                    _status.text = _model.Ranked ? RankedText.Widened : UiText.SearchingWider;
                    break;
                case QuickPlayViewModel.Stage.Starting:
                    _status.text = UiText.SearchingStart;
                    break;
                case QuickPlayViewModel.Stage.Bots:
                    _status.text = UiText.SearchingBots;
                    break;
                case QuickPlayViewModel.Stage.Found:
                    _status.text = UiText.FoundTable;
                    break;
                case QuickPlayViewModel.Stage.Failed:
                    _status.text = UiText.Error(_model.ErrorCode ?? "matchmaker");
                    break;
                default:
                    _status.text = "";
                    break;
            }
        }
    }
}
