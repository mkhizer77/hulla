using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Account
{
    /// <summary>What the account panel shows and does: the providers this build offers and the phone.</summary>
    public sealed class AccountOptions
    {
        public AccountOptions(IReadOnlyList<SocialProvider> providers, Func<SocialProvider, bool> linked, Func<bool> phoneLinked, Action<SocialProvider> link, Action phone)
        {
            Providers = providers;
            Linked = linked;
            PhoneLinked = phoneLinked;
            Link = link;
            Phone = phone;
        }

        public IReadOnlyList<SocialProvider> Providers { get; }

        public Func<SocialProvider, bool> Linked { get; }

        public Func<bool> PhoneLinked { get; }

        public Action<SocialProvider> Link { get; }

        /// <summary>Opens the phone number screen.</summary>
        public Action Phone { get; }

        public bool AnyLinked
        {
            get
            {
                if (PhoneLinked())
                {
                    return true;
                }

                foreach (var provider in Providers)
                {
                    if (Linked(provider))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Signing in and linking (concept: profile-guest, sheet-account, 30 Oct). A guest sees "Keep your
    /// progress" with Continue with Google, Facebook, Apple and Use phone number; once anything is linked
    /// the card shrinks to "Signed in with …" and Manage opens the Your account sheet, which lists every
    /// method with Linked or Link. The same sheet opens from Settings.
    /// </summary>
    public static class AccountPanel
    {
        /// <summary>Fills the Profile's slot under the name card.</summary>
        public static void Build(VisualElement host, AccountOptions options)
        {
            host.Clear();
            if (options.AnyLinked)
            {
                host.Add(SignedIn(host, options));
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("account-card");
            var title = new Label(UiText.KeepProgress);
            title.AddToClassList("account-card__title");
            card.Add(title);
            var line = new Label(UiText.KeepProgressLine);
            line.AddToClassList("account-card__line");
            card.Add(line);
            foreach (var provider in options.Providers)
            {
                var chosen = provider;
                card.Add(ProviderButton(Strings.Format(UiText.ContinueWith, UiText.Provider(chosen)), Key(chosen), () => options.Link(chosen)));
            }

            card.Add(ProviderButton(UiText.UsePhoneNumber, "phone", options.Phone));
            host.Add(card);
        }

        /// <summary>"Signed in with Google · Phone" and Manage, which opens the sheet.</summary>
        private static VisualElement SignedIn(VisualElement anyInside, AccountOptions options)
        {
            var names = new List<string>();
            foreach (var provider in options.Providers)
            {
                if (options.Linked(provider))
                {
                    names.Add(UiText.Provider(provider));
                }
            }

            if (options.PhoneLinked())
            {
                names.Add(UiText.PhoneNumber);
            }

            var row = new Button(() => OpenSheet(anyInside, options)) { text = "" };
            row.AddToClassList("account-signed");
            var tick = new Label("✓") { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("account-signed__tick");
            row.Add(tick);
            var text = new Label(Strings.Format(UiText.SignedInWith, string.Join(" · ", names))) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("account-signed__text");
            row.Add(text);
            var manage = new Label(UiText.Manage) { pickingMode = PickingMode.Ignore };
            manage.AddToClassList("account-signed__manage");
            row.Add(manage);
            return row;
        }

        /// <summary>Your account: every method, Linked with a tick or a yellow Link.</summary>
        public static void OpenSheet(VisualElement anyInside, AccountOptions options)
        {
            var panel = Navigation.Sheet.Open(anyInside, UiText.YourAccount);
            panel.AddToClassList("account-sheet");
            panel.Add(Navigation.Sheet.Line(UiText.YourAccountLine, "account-sheet__line"));
            foreach (var provider in options.Providers)
            {
                var chosen = provider;
                panel.Add(MethodRow(UiText.Provider(chosen), Key(chosen), options.Linked(chosen), () =>
                {
                    Navigation.Sheet.Close(panel);
                    options.Link(chosen);
                }));
            }

            panel.Add(MethodRow(UiText.PhoneNumber, "phone", options.PhoneLinked(), () =>
            {
                Navigation.Sheet.Close(panel);
                options.Phone();
            }));
            panel.Add(Navigation.Sheet.Line(UiText.ProgressStays, "account-sheet__foot"));
        }

        private static string Key(SocialProvider provider) => provider switch
        {
            SocialProvider.Apple => "apple",
            SocialProvider.Google => "google",
            _ => "facebook",
        };

        private static Button ProviderButton(string text, string key, Action tap)
        {
            var button = new Button(tap) { text = "", name = "account-" + key };
            button.AddToClassList("account-button");
            button.AddToClassList("account-button--" + key);
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("account-button__mark");
            button.Add(mark);
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("account-button__text");
            button.Add(label);
            return button;
        }

        private static VisualElement MethodRow(string name, string key, bool linked, Action link)
        {
            var row = new VisualElement { name = "method-" + key };
            row.AddToClassList("account-row");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("account-row__mark");
            mark.AddToClassList("account-row__mark--" + key);
            row.Add(mark);
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("account-row__name");
            row.Add(label);
            if (linked)
            {
                var done = new Label("✓ " + UiText.SocialLinked) { pickingMode = PickingMode.Ignore };
                done.AddToClassList("account-row__linked");
                row.Add(done);
            }
            else
            {
                var button = new Button(link) { text = UiText.Link };
                button.AddToClassList("account-row__link");
                row.Add(button);
            }

            return row;
        }
    }
}
