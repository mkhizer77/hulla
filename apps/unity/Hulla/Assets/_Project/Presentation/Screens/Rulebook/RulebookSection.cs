using System.Collections.Generic;

namespace Hulla.Presentation.Screens.Rulebook
{
    /// <summary>One section of a game's rulebook: an id to jump to, a title and plain sentences.</summary>
    public sealed class RulebookSection
    {
        public RulebookSection(string id, string title, IReadOnlyList<string> lines)
        {
            Id = id;
            Title = title;
            Lines = lines;
        }

        public string Id { get; }

        public string Title { get; }

        public IReadOnlyList<string> Lines { get; }
    }
}
