using System.Collections.Generic;
using Hulla.Application.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The avatar set (GDD 15.6): placeholder faces in the <c>avatars-base</c> Addressables group,
    /// which ships in the binary, one address per id, until the contractor art lands. A seat whose
    /// avatar is unknown keeps the tinted initial of its name.
    /// </summary>
    public static class AvatarArt
    {
        public const string ImageClass = "seat__avatar--image";

        /// <summary>Set once by the composition root; without it every seat shows its initial.</summary>
        public static IAssetPacks? Packs { get; set; }

        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>();

        /// <summary>The face for an avatar id, or null when the set has no such file.</summary>
        public static Texture2D? Find(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (Packs == null)
            {
                return null;
            }

            if (!Cache.TryGetValue(id!, out var texture))
            {
                texture = Packs.LoadLocal("avatars-base/" + id);
                Cache[id!] = texture;
            }

            return texture;
        }

        /// <summary>Puts the face on the circle, or the tinted initial of the name when there is none.</summary>
        public static void Apply(VisualElement avatar, Label initial, string? id, string name)
        {
            var face = Find(id);
            initial.text = name.Length == 0 ? "?" : name.Substring(0, 1).ToUpperInvariant();
            if (face == null)
            {
                Clear(avatar, initial);
                AvatarTint.Apply(avatar, name);
                return;
            }

            avatar.AddToClassList(ImageClass);
            avatar.style.backgroundImage = new StyleBackground(face);
            initial.style.display = DisplayStyle.None;
            foreach (var cls in AvatarTint.Classes)
            {
                avatar.RemoveFromClassList(cls);
            }
        }

        /// <summary>Back to a plain circle with the initial showing.</summary>
        public static void Clear(VisualElement avatar, Label initial)
        {
            avatar.RemoveFromClassList(ImageClass);
            avatar.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            initial.style.display = DisplayStyle.Flex;
            foreach (var cls in AvatarTint.Classes)
            {
                avatar.RemoveFromClassList(cls);
            }
        }
    }
}
