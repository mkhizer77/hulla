namespace Hulla.Application.Services
{
    /// <summary>One quick-chat phrase from content/quick-chat (GDD 13.3): the id on the wire and the text shown.</summary>
    public readonly struct ChatPhrase
    {
        public ChatPhrase(string id, string text, string translation, string tab)
        {
            Id = id;
            Text = text;
            Translation = translation;
            Tab = tab;
        }

        public string Id { get; }

        public string Text { get; }

        public string Translation { get; }

        public string Tab { get; }
    }
}
