using System;
using Hulla.Application.Settings;
using Hulla.Presentation.Theme;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// The one UI document in the Main scene. Swaps screen documents in with a short transition
    /// (GDD 15.7), keeps the bottom navigation, an open overlay and the top pill across swaps, and
    /// reports frame time to whoever owns the current screen (the table's animation scheduler).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShellView : MonoBehaviour
    {
        [SerializeField]
        private ScreenCatalog? _catalog;

        private VisualElement? _current;
        private NavBar? _nav;

        public event Action<float>? Ticked;

        public ScreenCatalog Catalog => _catalog != null ? _catalog : throw new InvalidOperationException("ShellView has no screen catalog");

        /// <summary>Swaps the 240 ms rise for a 120 ms fade (GDD 27).</summary>
        public bool ReduceMotion { get; set; }

        /// <summary>The table theme whose scene sits behind every screen (docs/design/table-themes.md).</summary>
        public TableSkin Skin { get; private set; }

        /// <summary>Switches the theme's scene on the screen and overlay showing now; later screens follow.</summary>
        public void ApplySkin(TableSkin skin, bool reduceMotion)
        {
            if (skin == Skin && reduceMotion == ReduceMotion && _current?.Q(AmbientLayer.ScreenName) != null)
            {
                return;
            }

            Skin = skin;
            ReduceMotion = reduceMotion;
            Decorate(_current);
            Decorate(Root.Q("shell-overlay"));
        }

        /// <summary>The theme's ambient layer goes behind the screen's own content.</summary>
        private void Decorate(VisualElement? instance)
        {
            var screen = instance?.Q(className: "screen");
            if (screen != null)
            {
                AmbientLayer.Decorate(screen, Skin, ReduceMotion);
            }
        }

        /// <summary>Raised when a bottom-navigation tab is tapped.</summary>
        public event Action<NavTab>? NavChosen;

        public VisualElement Show(VisualTreeAsset? screen)
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (screen == null)
            {
                throw new InvalidOperationException("screen document missing from the catalog");
            }

            // The navigation, an open overlay (settings) and the top pill stay across the swap.
            var overlay = root.Q("shell-overlay");
            var banner = root.Q("shell-banner");
            var nav = root.Q("shell-nav");
            root.Clear();
            // A right-to-left locale mirrors rows and side-aligned text (Theme/Rtl.uss, GDD 26.1).
            root.EnableInClassList("rtl", Text.Strings.RightToLeft);
            var instance = screen.Instantiate();
            instance.style.flexGrow = 1;
            instance.AddToClassList("screen-anim");
            instance.AddToClassList(ReduceMotion ? "screen--enter-fade" : "screen--enter");
            root.Add(instance);
            _current = instance;
            Decorate(instance);
            instance.schedule.Execute(() =>
            {
                instance.RemoveFromClassList("screen--enter");
                instance.RemoveFromClassList("screen--enter-fade");
            }).StartingIn(16);
            if (nav != null)
            {
                root.Add(nav);
            }

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

        /// <summary>Shows the bottom navigation with <paramref name="active"/> lit; call after <see cref="Show"/>.</summary>
        public void ShowNav(NavTab active)
        {
            var root = Root;
            if (_nav == null)
            {
                _nav = new NavBar(Text.UiText.NavLabel);
                _nav.Chosen += tab => NavChosen?.Invoke(tab);
            }

            if (_nav.parent != root)
            {
                _nav.RemoveFromHierarchy();
                var overlay = root.Q("shell-overlay");
                var index = overlay != null ? root.IndexOf(overlay) : root.childCount;
                root.Insert(Math.Max(0, Math.Min(index, root.childCount)), _nav);
            }

            _nav.Select(active);
            root.AddToClassList(WithNavClass);
        }

        public void HideNav()
        {
            _nav?.RemoveFromHierarchy();
            Root.RemoveFromClassList(WithNavClass);
        }

        /// <summary>On the root while the tabs show, so a wide layout can make room for them as a side rail.</summary>
        public const string WithNavClass = "shell--with-nav";

        /// <summary>A count on a tab, shown whether or not the bar is on screen right now.</summary>
        public void SetNavBadge(NavTab tab, int count)
        {
            if (_nav == null)
            {
                _nav = new NavBar(Text.UiText.NavLabel);
                _nav.Chosen += chosen => NavChosen?.Invoke(chosen);
            }

            _nav.SetBadge(tab, count);
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
            instance.AddToClassList("screen-anim");
            instance.AddToClassList(ReduceMotion ? "screen--enter-fade" : "screen--enter");
            Root.Add(instance);
            Decorate(instance);
            instance.schedule.Execute(() =>
            {
                instance.RemoveFromClassList("screen--enter");
                instance.RemoveFromClassList("screen--enter-fade");
            }).StartingIn(16);
            return instance;
        }

        public void CloseOverlay() => Root.Q("shell-overlay")?.RemoveFromHierarchy();

        /// <summary>A question with two answers over the current screen; either answer closes it.</summary>
        public void ShowDialog(string question, string yes, string no, Action onYes, Action onNo)
        {
            Root.Q("shell-dialog")?.RemoveFromHierarchy();
            Root.Add(Dialog(question, yes, no, onYes, onNo));
        }

        /// <summary>The dialog element itself; either answer removes it.</summary>
        public static VisualElement Dialog(string question, string yes, string no, Action onYes, Action onNo)
        {
            var overlay = new VisualElement { name = "shell-dialog" };
            overlay.AddToClassList("overlay");
            overlay.AddToClassList("results");
            overlay.style.display = DisplayStyle.Flex;
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("dialog");
            var title = new Label(question);
            title.AddToClassList("title");
            title.AddToClassList("dialog__text");
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
            noButton.AddToClassList("dialog__button");
            yesButton.AddToClassList("dialog__button");
            panel.Add(yesButton);
            panel.Add(noButton);
            overlay.Add(panel);
            return overlay;
        }

        /// <summary>A pill at the top of whatever screen is showing (GDD 14.5: pills, never modals); null hides it.</summary>
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

        private void Awake()
        {
            // A copy, so turning the reference on rotation never edits the asset; Expand per brief §2.2.
            var document = GetComponent<UIDocument>();
            if (document.panelSettings != null)
            {
                var settings = Instantiate(document.panelSettings);
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                settings.screenMatchMode = PanelScreenMatchMode.Expand;
                document.panelSettings = settings;
            }

            Relayout();
        }

        /// <summary>The size class the shell is laid out for now.</summary>
        public SizeClass Size { get; private set; }

        /// <summary>The safe-area padding on the shell now, in panel units.</summary>
        public Insets SafeArea { get; private set; }

        /// <summary>Raised after a resize or rotation changed the size class or the insets.</summary>
        public event Action? LayoutChanged;

        private Vector2Int _screen;
        private Rect _safeArea;

        /// <summary>
        /// Turns the reference with the screen, sets the size class and pads the shell by the safe area.
        /// Only styles change, so a rotation mid-round keeps every screen and the table as they are.
        /// </summary>
        private void Relayout()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (screen == _screen && safeArea == _safeArea)
            {
                return;
            }

            var physicalChange = _screen != default && Mathf.Min(screen.x, screen.y) != Mathf.Min(_screen.x, _screen.y);
            _screen = screen;
            _safeArea = safeArea;
            if (physicalChange)
            {
                // A foldable opened or closed, or a split-screen window changed: phone or tablet again.
                OrientationPolicy.Apply();
            }

            var settings = GetComponent<UIDocument>().panelSettings;
            if (settings != null)
            {
                settings.referenceResolution = ScreenLayout.Reference(screen.x, screen.y);
            }

            Size = ScreenLayout.ClassOf(ScreenLayout.Canvas(screen.x, screen.y).x);
            SafeArea = ScreenLayout.SafeInsets(safeArea, screen.x, screen.y);
            ScreenLayout.Apply(Root, Size, SafeArea);
            LayoutChanged?.Invoke();
        }

        private void Update() => Ticked?.Invoke(Time.deltaTime);

        private void LateUpdate()
        {
            Relayout();
            KeyboardAvoidance.Apply(Root, _current);
            KeyboardAvoidance.Apply(Root, Root.Q("shell-overlay"));
        }
    }
}
