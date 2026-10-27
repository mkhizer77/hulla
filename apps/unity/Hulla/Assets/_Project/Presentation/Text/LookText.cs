namespace Hulla.Presentation.Text
{
    /// <summary>The look playtest: the Home card and the question after a round.</summary>
    public static class LookText
    {
        public static string CardTitle => Strings.Text("looks.card_title");
        public static string CardLine => Strings.Text("looks.card_line");
        public static string Ask => Strings.Text("looks.ask");
        public static string Keep => Strings.Text("looks.keep");
        public static string Back => Strings.Text("looks.back");
        public static string Comment => Strings.Text("looks.comment");
        public static string Send => Strings.Text("looks.send");
        public static string Thanks => Strings.Text("looks.thanks");
        public static string RateThis => Strings.Text("looks.rate_this");

        /// <summary>The answer for 1 (don't like it) to 5 (love it).</summary>
        public static string Rating(int stars) => stars switch
        {
            5 => Strings.Text("looks.rate_5"),
            4 => Strings.Text("looks.rate_4"),
            3 => Strings.Text("looks.rate_3"),
            2 => Strings.Text("looks.rate_2"),
            _ => Strings.Text("looks.rate_1"),
        };
    }
}
