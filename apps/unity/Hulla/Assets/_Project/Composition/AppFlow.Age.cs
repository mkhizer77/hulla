using System;
using Hulla.Application.Age;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Composition
{
    /// <summary>
    /// The age question (GDD 24.6, 25.4, #87): a neutral birth-year field before anything else on the
    /// first launch, with no hint of the age that matters. Under 13 the app stays closed on this phone;
    /// otherwise the year goes to the server after sign-in, which applies the limits for younger players.
    /// </summary>
    public sealed partial class AppFlow
    {
        /// <summary>Asks once; false when the player is under 13.</summary>
        private Awaitable<bool> AskAgeAsync()
        {
            var answered = new AwaitableCompletionSource<bool>();
            var plate = _shell.ShowGate(UiText.AgeTitle);
            plate.Add(Line(UiText.AgeLine, "gate__line"));
            var field = new TextField { maxLength = 4, keyboardType = TouchScreenKeyboardType.NumberPad };
            field.AddToClassList("gate__name");
            plate.Add(field);
            var guard = Line("", "gate__guard");
            plate.Add(guard);
            var go = new Button(() =>
            {
                var now = DateTime.UtcNow;
                if (!int.TryParse(field.value.Trim(), out var year) || !AgeRules.Valid(year, now))
                {
                    guard.text = UiText.AgeInvalid;
                    return;
                }

                _shell.CloseGate();
                if (AgeRules.TooYoung(year, now))
                {
                    _app.Settings.AgeBlocked = true;
                    answered.SetResult(false);
                    return;
                }

                _app.Settings.BirthYear = year;
                _app.Ads.UnderEighteen = AgeRules.UnderEighteen(year, now);
                answered.SetResult(true);
            }) { text = UiText.Continue };
            go.AddToClassList("gate__go");
            plate.Add(go);
            return answered.Awaitable;
        }

        /// <summary>The closed door for under-13s: no way on, and no account is made.</summary>
        private void ShowAgeBlocked()
        {
            var plate = _shell.ShowGate(UiText.AgeBlocked);
            plate.Add(Line(UiText.AgeBlockedLine, "gate__line"));
        }

        /// <summary>After a sign-in: the year goes to the server once, and its answer sets the ads (GDD 25.7).</summary>
        private async void SyncAge()
        {
            var year = _app.Settings.BirthYear;
            _app.Ads.UnderEighteen = AgeRules.UnderEighteen(year, DateTime.UtcNow);
            if (year == null || _app.Settings.BirthYearSent)
            {
                return;
            }

            var sent = await _app.Age.SetAsync(year.Value, _app.Lifetime);
            if (sent.IsOk)
            {
                _app.Settings.BirthYearSent = true;
                _app.Ads.UnderEighteen = !sent.Value.PersonalisedAds;
            }
            else if (sent.Error.Code == "profile.age_locked")
            {
                // The account already has a year from another phone; the server's limits apply.
                _app.Settings.BirthYearSent = true;
            }
        }
    }
}
