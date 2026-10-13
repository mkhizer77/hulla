using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The placeholder avatar colour for a name (GDD 15.6 until the avatar set lands): stable across
    /// runs and platforms, so a friend keeps their colour on every screen.
    /// </summary>
    public static class AvatarTint
    {
        public static readonly string[] Classes = { "seat__avatar--a", "seat__avatar--b", "seat__avatar--c", "seat__avatar--d", "seat__avatar--e", "seat__avatar--f" };

        public static string ClassFor(string name) => Classes[(int)((uint)StableHash(name) % (uint)Classes.Length)];

        public static void Apply(VisualElement avatar, string name)
        {
            var tint = ClassFor(name);
            foreach (var cls in Classes)
            {
                avatar.EnableInClassList(cls, cls == tint);
            }
        }

        /// <summary>The same on every run and platform, unlike string.GetHashCode.</summary>
        public static int StableHash(string text)
        {
            var hash = 17;
            foreach (var c in text)
            {
                hash = unchecked(hash * 31 + c);
            }

            return hash;
        }
    }
}
