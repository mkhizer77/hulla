using System;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.QuickPlay
{
    /// <summary>The search: the table size asked for, the seconds so far, what the search is doing now, and Cancel.</summary>
    public sealed class QuickPlayView : IDisposable
    {
        private readonly QuickPlayViewModel _model;
        private readonly Label _title;
        private readonly Label _elapsed;
        private readonly Label _status;
        private int _shownSeconds = -1;

        public QuickPlayView(VisualElement root, QuickPlayViewModel model, Action cancel)
        {
            _model = model;
            _title = root.Q<Label>("title");
            _elapsed = root.Q<Label>("elapsed");
            _status = root.Q<Label>("status");
            var cancelButton = root.Q<Button>("cancel");
            cancelButton.text = UiText.Cancel;
            cancelButton.clicked += cancel;
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _title.text = string.Format(UiText.Searching, _model.Seats);
            var seconds = (int)_model.Elapsed;
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _elapsed.text = seconds + "s";
            }

            switch (_model.Current)
            {
                case QuickPlayViewModel.Stage.Widened:
                    _status.text = UiText.SearchingWider;
                    break;
                case QuickPlayViewModel.Stage.Starting:
                    _status.text = UiText.SearchingStart;
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
