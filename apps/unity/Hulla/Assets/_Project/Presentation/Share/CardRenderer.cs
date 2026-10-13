using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hulla.Presentation.Share
{
    /// <summary>The two share-card shapes (GDD 10.9): a 4:5 feed post and a 9:16 story.</summary>
    public enum CardRatio
    {
        Feed,
        Story,
    }

    /// <summary>
    /// Draws a UI Toolkit tree off screen at full size and returns it as a PNG: a copy of the app's
    /// panel settings (same theme) aimed at a render texture, read back after it has painted.
    /// </summary>
    public static class CardRenderer
    {
        public const int Width = 1080;

        public static int HeightOf(CardRatio ratio) => ratio == CardRatio.Story ? 1920 : 1350;

        public static async Awaitable<byte[]> RenderAsync(PanelSettings template, CardRatio ratio, Action<VisualElement> build)
        {
            var height = HeightOf(ratio);
            var texture = new RenderTexture(Width, height, 24, RenderTextureFormat.ARGB32);
            var settings = Object.Instantiate(template);
            settings.targetTexture = texture;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            settings.clearColor = true;
            settings.colorClearValue = Color.black;
            var host = new GameObject("share-card") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                var root = document.rootVisualElement;
                root.style.width = Width;
                root.style.height = height;
                build(root);

                // Frames to lay out and paint into the texture (end of frame never comes in batch mode).
                for (var frame = 0; frame < 3; frame++)
                {
                    await Awaitable.NextFrameAsync();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(Width, height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, Width, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                var png = pixels.EncodeToPNG();
                Object.Destroy(pixels);
                return png;
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(settings);
                texture.Release();
                Object.Destroy(texture);
            }
        }
    }
}
