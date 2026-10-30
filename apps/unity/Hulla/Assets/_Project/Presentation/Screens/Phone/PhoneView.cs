using System;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Phone
{
    /// <summary>
    /// The phone number screen (GDD 10.2, #99): type a number, get a code, confirm it. A number this
    /// account holds can be removed; one another account holds offers to switch to it.
    /// </summary>
    public sealed class PhoneView : IDisposable
    {
        private readonly PhoneViewModel _model;
        private readonly VisualElement _numberStep;
        private readonly VisualElement _codeStep;
        private readonly VisualElement _linkedStep;
        private readonly VisualElement _offerStep;
        private readonly TextField _number;
        private readonly TextField _code;
        private readonly Label _sent;
        private readonly Label _status;
        private readonly Button[] _actions;

        public PhoneView(VisualElement root, PhoneViewModel model, Action back, PhoneActions act)
        {
            _model = model;
            root.Q<Label>("title").text = Strings.Text("phone.title");
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            root.Q<Label>("intro").text = Strings.Text("phone.intro");
            _numberStep = root.Q<VisualElement>("number-step");
            _codeStep = root.Q<VisualElement>("code-step");
            _linkedStep = root.Q<VisualElement>("linked-step");
            _offerStep = root.Q<VisualElement>("offer-step");
            _number = root.Q<TextField>("number");
            _number.label = Strings.Text("phone.number_label");
            _number.textEdition.placeholder = UiText.PhonePlaceholder;
            _number.keyboardType = TouchScreenKeyboardType.PhonePad;
            _number.maxLength = 20;
            _code = root.Q<TextField>("code");
            _code.label = Strings.Text("phone.code_label");
            _code.keyboardType = TouchScreenKeyboardType.NumberPad;
            _code.maxLength = 8;
            _sent = root.Q<Label>("sent");
            _status = root.Q<Label>("status");
            var send = Button(root, "send", Strings.Text("phone.send"), () => act.Send(_number.value));
            var verify = Button(root, "verify", Strings.Text("phone.verify"), () => act.Verify(_code.value));
            var other = Button(root, "other-number", Strings.Text("phone.other_number"), act.StartOver);
            var remove = Button(root, "remove", Strings.Text("phone.remove"), act.Remove);
            var switchButton = Button(root, "switch", Strings.Text("phone.switch"), act.Switch);
            var decline = Button(root, "decline", UiText.Cancel, act.StartOver);
            root.Q<Label>("linked").text = Strings.Text("phone.linked");
            root.Q<Label>("offer").text = Strings.Text("phone.offer");
            _actions = new[] { send, verify, other, remove, switchButton, decline };
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private static Button Button(VisualElement root, string name, string text, Action clicked)
        {
            var button = root.Q<Button>(name);
            button.text = text;
            button.clicked += clicked;
            return button;
        }

        private void Refresh()
        {
            var stage = _model.Current;
            Show(_numberStep, stage == PhoneViewModel.Stage.NoNumber);
            Show(_codeStep, stage == PhoneViewModel.Stage.CodeSent);
            Show(_linkedStep, stage == PhoneViewModel.Stage.Linked);
            Show(_offerStep, stage == PhoneViewModel.Stage.Offer);
            foreach (var action in _actions)
            {
                action.SetEnabled(!_model.Busy);
            }

            if (stage == PhoneViewModel.Stage.CodeSent)
            {
                _sent.text = Strings.Get("phone.sent", _model.MaskedPhone);
                _code.value = "";
            }

            _status.text = _model.ErrorCode != null
                ? UiText.Error(_model.ErrorCode)
                : _model.Busy
                    ? UiText.Connecting
                    : stage == PhoneViewModel.Stage.Switched
                        ? Strings.Text("phone.switched")
                        : _model.Removed ? Strings.Text("phone.removed") : "";
        }

        private static void Show(VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>What the buttons do, wired by the composition root.</summary>
        public sealed class PhoneActions
        {
            public PhoneActions(Action<string> send, Action<string> verify, Action remove, Action @switch, Action startOver)
            {
                Send = send;
                Verify = verify;
                Remove = remove;
                Switch = @switch;
                StartOver = startOver;
            }

            public Action<string> Send { get; }

            public Action<string> Verify { get; }

            public Action Remove { get; }

            public Action Switch { get; }

            public Action StartOver { get; }
        }
    }
}
