namespace Hulla.Application.Services
{
    /// <summary>One quick-chat phrase from content/quick-chat (GDD 13.3): the id on the wire, the text shown, and when it fits.</summary>
    public readonly struct ChatPhrase
    {
        /// <param name="context">When the phrase fits (`gave_thulla`, `opponent_slow`, `any`…); drives the table's strip.</param>
        public ChatPhrase(string id, string text, string translation, string tab, string context = "any")
        {
            Id = id;
            Text = text;
            Translation = translation;
            Tab = tab;
            Context = context;
        }

        public string Id { get; }

        public string Text { get; }

        public string Translation { get; }

        public string Tab { get; }

        public string Context { get; }
    }
}
