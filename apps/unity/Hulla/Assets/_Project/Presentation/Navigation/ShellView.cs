using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// The one UI document in the Main scene. Swaps screen documents in and reports frame time to
    /// whoever owns the current screen (the table's animation scheduler).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShellView : MonoBehaviour
    {
        [SerializeField]
        private ScreenCatalog? _catalog;

        public event Action<float>? Ticked;

        public ScreenCatalog Catalog => _catalog != null ? _catalog : throw new InvalidOperationException("ShellView has no screen catalog");

        public VisualElement Show(VisualTreeAsset? screen)
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (screen == null)
            {
                throw new InvalidOperationException("screen document missing from the catalog");
            }

            // An open overlay (settings) and the reconnect banner stay on top of the new screen.
            var overlay = root.Q("shell-overlay");
            var banner = root.Q("shell-banner");
            root.Clear();
            var instance = screen.Instantiate();
            instance.style.flexGrow = 1;
            root.Add(instance);
            if (overlay != null)
            {
                root.Add(overlay);
            }

            if (banner != null)
            {
                root.Add(banner);
            }

            return instance;
        }

        /// <summary>The app's panel settings, copied for off-screen drawing (share cards).</summary>
        public PanelSettings PanelSettings => GetComponent<UIDocument>().panelSettings;

        /// <summary>The document root, where the theme classes from the settings live.</summary>
        public VisualElement Root => GetComponent<UIDocument>().rootVisualElement;

        /// <summary>Puts a screen over the current one without replacing it (settings at the table).</summary>
        public VisualElement ShowOverlay(VisualTreeAsset? screen)
        {
            CloseOverlay();
            if (screen == null)
            {
                throw new InvalidOperationException("screen document missing from the catalog");
            }

            var instance = screen.Instantiate();
            instance.name = "shell-overlay";
            instance.AddToClassList("overlay");
            Root.Add(instance);
            return instance;
        }

        public void CloseOverlay() => Root.Q("shell-overlay")?.RemoveFromHierarchy();

        /// <summary>A question with two answers over the current screen; either answer closes it.</summary>
        public void ShowDialog(string question, string yes, string no, Action onYes, Action onNo)
        {
            Root.Q("shell-dialog")?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = "shell-dialog" };
            overlay.AddToClassList("overlay");
            overlay.AddToClassList("results");
            overlay.style.display = DisplayStyle.Flex;
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            var title = new Label(question);
            title.AddToClassList("title");
            title.AddToClassList("title--small");
            panel.Add(title);
            var yesButton = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onYes();
            }) { text = yes };
            yesButton.AddToClassList("button");
            yesButton.AddToClassList("button--primary");
            var noButton = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onNo();
            }) { text = no };
            noButton.AddToClassList("button");
            panel.Add(yesButton);
            panel.Add(noButton);
            overlay.Add(panel);
            Root.Add(overlay);
        }

        /// <summary>A strip across the top of whatever screen is showing; null hides it.</summary>
        public void SetBanner(string? text)
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var banner = root.Q<Label>("shell-banner");
            if (text == null)
            {
                banner?.RemoveFromHierarchy();
                return;
            }

            if (banner == null)
            {
                banner = new Label { name = "shell-banner" };
                banner.AddToClassList("banner");
                root.Add(banner);
            }

            banner.text = text;
        }

        private void Update() => Ticked?.Invoke(Time.deltaTime);
    }
}
