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
            root.Clear();
            if (screen == null)
            {
                throw new InvalidOperationException("screen document missing from the catalog");
            }

            var instance = screen.Instantiate();
            instance.style.flexGrow = 1;
            root.Add(instance);
            return instance;
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
