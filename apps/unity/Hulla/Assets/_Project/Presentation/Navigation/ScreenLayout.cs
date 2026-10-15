using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>The layout a canvas width gets (docs/design/ui-redesign-brief.md §2.3).</summary>
    public enum SizeClass
    {
        Compact,
        Medium,
        Expanded,
    }

    /// <summary>Space to keep clear of notches, system bars and the home indicator, in panel units.</summary>
    public readonly struct Insets
    {
        public Insets(float top, float right, float bottom, float left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        public float Top { get; }

        public float Right { get; }

        public float Bottom { get; }

        public float Left { get; }

        public override string ToString() => $"top {Top}, right {Right}, bottom {Bottom}, left {Left}";
    }

    /// <summary>
    /// The maths behind the shell's layout (brief §2.2 to §2.4): scale with Expand against a 1080 × 1920
    /// reference in portrait and 1920 × 1080 in landscape, the size class from the canvas width, and the
    /// safe area turned into panel-unit insets. Pure, so the thresholds are tested without a screen.
    /// </summary>
    public static class ScreenLayout
    {
        public const int ShortSide = 1080;
        public const int LongSide = 1920;
        public const float MediumFrom = 1200f;
        public const float ExpandedFrom = 1800f;

        public const string CompactClass = "size--compact";
        public const string MediumClass = "size--medium";
        public const string ExpandedClass = "size--expanded";

        /// <summary>The reference canvas for a screen: portrait unless the screen is wider than tall.</summary>
        public static Vector2Int Reference(int screenWidth, int screenHeight) =>
            screenWidth > screenHeight ? new Vector2Int(LongSide, ShortSide) : new Vector2Int(ShortSide, LongSide);

        /// <summary>Expand: the whole reference fits and the canvas grows in the other direction.</summary>
        public static float Scale(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return 1f;
            }

            var reference = Reference(screenWidth, screenHeight);
            return Mathf.Min(screenWidth / (float)reference.x, screenHeight / (float)reference.y);
        }

        /// <summary>The canvas the screens lay out in, in panel units.</summary>
        public static Vector2 Canvas(int screenWidth, int screenHeight)
        {
            var scale = Scale(screenWidth, screenHeight);
            return new Vector2(screenWidth / scale, screenHeight / scale);
        }

        /// <summary>
        /// Compact under 1200, Medium to 1799, Expanded from 1800. A landscape canvas is at least 1920 wide,
        /// so Medium only happens in portrait.
        /// </summary>
        public static SizeClass ClassOf(float canvasWidth)
        {
            if (canvasWidth >= ExpandedFrom)
            {
                return SizeClass.Expanded;
            }

            return canvasWidth >= MediumFrom ? SizeClass.Medium : SizeClass.Compact;
        }

        public static string ClassName(SizeClass size) => size switch
        {
            SizeClass.Medium => MediumClass,
            SizeClass.Expanded => ExpandedClass,
            _ => CompactClass,
        };

        /// <summary>
        /// The safe area as insets on each side, in panel units. <paramref name="safeArea"/> is in screen
        /// pixels with the origin at the bottom left, as <see cref="Screen.safeArea"/> gives it.
        /// </summary>
        public static Insets SafeInsets(Rect safeArea, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return default;
            }

            var scale = Scale(screenWidth, screenHeight);
            return new Insets(
                Mathf.Max(0f, screenHeight - safeArea.yMax) / scale,
                Mathf.Max(0f, screenWidth - safeArea.xMax) / scale,
                Mathf.Max(0f, safeArea.yMin) / scale,
                Mathf.Max(0f, safeArea.xMin) / scale);
        }

        /// <summary>Puts the size class on the shell root (one of the three, never two) and pads it by the insets.</summary>
        public static void Apply(VisualElement root, SizeClass size, Insets insets)
        {
            root.EnableInClassList(CompactClass, size == SizeClass.Compact);
            root.EnableInClassList(MediumClass, size == SizeClass.Medium);
            root.EnableInClassList(ExpandedClass, size == SizeClass.Expanded);
            root.style.paddingTop = insets.Top;
            root.style.paddingRight = insets.Right;
            root.style.paddingBottom = insets.Bottom;
            root.style.paddingLeft = insets.Left;
        }
    }

    /// <summary>
    /// Phones stay in portrait; tablets rotate freely (brief §2.1). A tablet is a screen whose short side
    /// is at least 600 dp, the line Android draws for tablet layouts.
    /// </summary>
    public static class OrientationPolicy
    {
        public const float TabletShortSideDp = 600f;

        public static bool IsTablet(int screenWidth, int screenHeight, float dpi)
        {
            if (dpi <= 0f)
            {
                return false;
            }

            var shortSideDp = Mathf.Min(screenWidth, screenHeight) / dpi * 160f;
            return shortSideDp >= TabletShortSideDp;
        }

        /// <summary>Locks a phone to portrait or frees a tablet; again when a foldable opens or closes.</summary>
        public static void Apply()
        {
            if (UnityEngine.Application.isEditor)
            {
                return;
            }

            if (IsTablet(Screen.width, Screen.height, Screen.dpi))
            {
                Screen.autorotateToPortrait = true;
                Screen.autorotateToPortraitUpsideDown = true;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;

                // Reading Screen.orientation gives the current one, never AutoRotation, so set it each time.
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
            else if (Screen.orientation != ScreenOrientation.Portrait)
            {
                Screen.orientation = ScreenOrientation.Portrait;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void OnLaunch() => Apply();
    }
}
