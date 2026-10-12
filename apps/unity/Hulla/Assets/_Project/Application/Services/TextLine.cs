namespace Hulla.Application.Services
{
    /// <summary>One free-text message on the table channel.</summary>
    public readonly struct TextLine
    {
        public TextLine(string senderId, string senderName, string text)
        {
            SenderId = senderId;
            SenderName = senderName;
            Text = text;
        }

        public string SenderId { get; }

        public string SenderName { get; }

        public string Text { get; }
    }
}
