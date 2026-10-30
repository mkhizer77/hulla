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
            root.Q(Sheet.Name)?.RemoveFromHierarchy();
            var overlay = root.Q("shell-overlay");
            var banner = root.Q("shell-banner");
            var nav = root.Q("shell-nav");
            root.Clear();
            // A right-to-left locale mirrors rows and side-aligned text (Theme/Rtl.uss, GDD 26.1).
            root.EnableInClassList("rtl", Text.Strings.RightToLeft);
            // The text sprites follow the language (Theme/TextArt.cs).
            foreach (var name in new System.Collections.Generic.List<string>(root.GetClasses()))
            {
                if (name.StartsWith("lang--", StringComparison.Ordinal))
                {
                    root.RemoveFromClassList(name);
                }
            }

            root.AddToClassList(TextArt.LanguageClass(Text.Strings.Locale));
            // The paragraph direction, so a line ending in Latin or punctuation still reads right to left (#132).
            root.languageDirection = Text.Strings.RightToLeft ? LanguageDirection.RTL : LanguageDirection.Inherit;
            var instance = screen.Instantiate();
            instance.style.flexGrow = 1;
            instance.AddToClassList("screen-anim");
            instance.AddToClassList(ReduceMotion ? "screen--enter-fade" : "screen--enter");
            root.Add(instance);
            _current = instance;
            Backdrop.Set(root, Backdrop.For(instance));
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
        }

        public void HideNav() => _nav?.RemoveFromHierarchy();

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
            instance.AddToClassList(Backdrop.CalmClass);
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
        /// <param name="danger">A destructive yes (delete the account) wears the red button, not the yellow one.</param>
        public void ShowDialog(string question, string yes, string no, Action onYes, Action onNo, bool danger = false)
        {
            Root.Q("shell-dialog")?.RemoveFromHierarchy();
            Root.Add(Dialog(question, yes, no, onYes, onNo, danger));
        }

        /// <summary>
        /// The dialog element itself (design: every question is a bottom sheet over the dimmed screen,
        /// the safe answer left, the other right, red when destructive); either answer removes it.
        /// </summary>
        public static VisualElement Dialog(string question, string yes, string no, Action onYes, Action onNo, bool danger = false)
        {
            var overlay = new VisualElement { name = "shell-dialog" };
            overlay.AddToClassList("overlay");
            overlay.AddToClassList("sheet-scrim");
            var panel = new VisualElement();
            panel.AddToClassList("sheet");
            var grabber = new VisualElement { pickingMode = PickingMode.Ignore };
            grabber.AddToClassList("sheet__grabber");
            panel.Add(grabber);
            var (head, body) = SplitQuestion(question);
            var title = new Label(head);
            title.AddToClassList("sheet__title");
            title.AddToClassList("dialog__text");
            panel.Add(title);
            if (body.Length > 0)
            {
                var text = new Label(body);
                text.AddToClassList("sheet__body");
                panel.Add(text);
            }

            var answers = new VisualElement();
            answers.AddToClassList("sheet__answers");
            var noButton = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onNo();
            }) { text = no };
            noButton.AddToClassList("sheet__answer");
            noButton.AddToClassList("dialog__button");
            var yesButton = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onYes();
            }) { text = yes };
            yesButton.AddToClassList("sheet__answer");
            yesButton.AddToClassList("dialog__button");
            yesButton.AddToClassList(danger ? "sheet__answer--danger" : "sheet__answer--yes");
            yesButton.AddToClassList(danger ? "button--danger" : "button--primary");
            answers.Add(noButton);
            answers.Add(yesButton);
            panel.Add(answers);
            overlay.Add(panel);
            return overlay;
        }

        /// <summary>The first sentence as the sheet's title and the rest as its line, when the first is short.</summary>
        public static (string Title, string Body) SplitQuestion(string question)
        {
            for (var i = 0; i < question.Length - 1; i++)
            {
                var c = question[i];
                if ((c == '?' || c == '.' || c == '!') && question[i + 1] == ' ' && i < 64)
                {
                    var rest = question.Substring(i + 2).Trim();
                    if (rest.Length > 0)
                    {
                        return (question.Substring(0, i + 1), rest);
                    }
                }
            }

            return (question, "");
        }

        /// <summary>
        /// A first-launch step (design: First launch): one plate on the calm sky, one question each. The
        /// caller fills the plate; <see cref="CloseGate"/> takes it away.
        /// </summary>
        public VisualElement ShowGate(string title, string? picture = null)
        {
            CloseGate();
            var overlay = new VisualElement { name = "shell-gate" };
            overlay.AddToClassList("overlay");
            overlay.AddToClassList("gate");
            overlay.AddToClassList(Backdrop.CalmClass);
            var plate = new VisualElement();
            plate.AddToClassList("gate__plate");
            if (picture != null)
            {
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("gate__picture");
                art.AddToClassList("gate__picture--" + picture);
                plate.Add(art);
            }

            var label = new Label(title);
            label.AddToClassList("gate__title");
            plate.Add(label);
            overlay.Add(plate);
            Root.Add(overlay);
            Decorate(overlay);
            return plate;
        }

        public void CloseGate() => Root.Q("shell-gate")?.RemoveFromHierarchy();

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
            Root.AddToClassList("shell-root");
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
            var safeArea = FakeNotch ? NotchSafeArea(screen) : Screen.safeArea;
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

        /// <summary>
        /// Development builds only: <c>-hulla-notch</c> gives a desktop window the iPhone 17 Pro's safe area
        /// (62 pt clear at the top, 34 pt at the bottom of 874 pt) and draws the island and the home bar,
        /// so notch spacing is checked without a phone.
        /// </summary>
        private static bool? _fakeNotch;

        private static bool FakeNotch => _fakeNotch ??= Debug.isDebugBuild && Array.IndexOf(Environment.GetCommandLineArgs(), "-hulla-notch") >= 0;

        private static Rect NotchSafeArea(Vector2Int screen) =>
            new Rect(0f, screen.y * 34f / 874f, screen.x, screen.y * (874f - 62f - 34f) / 874f);

        private void DrawFakeNotch()
        {
            if (!FakeNotch)
            {
                return;
            }

            var island = Root.Q("dev-island");
            if (island == null)
            {
                island = DevMark("dev-island", 34.3f, 1.26f, 31.4f, 4.2f, new Color(0f, 0f, 0f, 1f));
                Root.Add(island);
                Root.Add(DevMark("dev-homebar", 32f, 98.6f, 36f, 0.6f, new Color(0f, 0f, 0f, 0.8f)));
            }

            island.BringToFront();
            Root.Q("dev-homebar")?.BringToFront();
        }

        private static VisualElement DevMark(string name, float left, float top, float width, float height, Color color)
        {
            var mark = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            mark.style.position = Position.Absolute;
            mark.style.left = Length.Percent(left);
            mark.style.top = Length.Percent(top);
            mark.style.width = Length.Percent(width);
            mark.style.height = Length.Percent(height);
            mark.style.backgroundColor = color;
            var radius = new StyleLength(new Length(60f));
            mark.style.borderTopLeftRadius = radius;
            mark.style.borderTopRightRadius = radius;
            mark.style.borderBottomLeftRadius = radius;
            mark.style.borderBottomRightRadius = radius;
            return mark;
        }

        /// <summary>
        /// Overlays fill the whole screen (their scene or dim reaches under the notch) and keep their
        /// content inside the safe area, like the screens the root pads.
        /// </summary>
        private void PadToSafeArea(VisualElement? overlay)
        {
            if (overlay == null)
            {
                return;
            }

            var insets = SafeArea;
            if (Math.Abs(overlay.style.paddingTop.value.value - insets.Top) < 0.5f
                && Math.Abs(overlay.style.paddingBottom.value.value - insets.Bottom) < 0.5f
                && overlay.style.paddingTop.keyword != StyleKeyword.Null)
            {
                return;
            }

            overlay.style.paddingTop = insets.Top;
            overlay.style.paddingBottom = insets.Bottom;
            overlay.style.paddingLeft = insets.Left;
            overlay.style.paddingRight = insets.Right;
        }

        private void Update() => Ticked?.Invoke(Time.deltaTime);

        private void LateUpdate()
        {
            Relayout();
            KeyboardAvoidance.Apply(Root, _current);
            KeyboardAvoidance.Apply(Root, Root.Q("shell-overlay"));
            PadToSafeArea(Root.Q("shell-overlay"));
            PadToSafeArea(Root.Q("shell-dialog"));
            PadToSafeArea(Root.Q(Sheet.Name));
            PadToSafeArea(Root.Q("shell-gate"));
            var banner = Root.Q("shell-banner");
            if (banner != null && Math.Abs(banner.style.marginTop.value.value - SafeArea.Top) > 0.5f)
            {
                banner.style.marginTop = SafeArea.Top;
            }

            DrawFakeNotch();
        }
    }
}
