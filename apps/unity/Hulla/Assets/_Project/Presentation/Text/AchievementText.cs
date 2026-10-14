namespace Hulla.Presentation.Text
{
    /// <summary>
    /// Achievement names and goals from the string tables (GDD 12.7, 26.1): achievement.&lt;id&gt;.name,
    /// achievement.goal.&lt;metric&gt; with the target as its plural argument; an id without a name
    /// falls back to its goal.
    /// </summary>
    public static class AchievementText
    {
        public static string Name(string id, string metric, long target) =>
            Strings.Has("achievement." + id + ".name") ? Strings.Text("achievement." + id + ".name") : Goal(metric, target);

        public static string Goal(string metric, long target) =>
            Strings.Has("achievement.goal." + metric) ? Strings.Get("achievement.goal." + metric, target) : UiText.MissionText(metric, target);

        public static string Category(string category) =>
            Strings.Has("achievement.category." + category) ? Strings.Text("achievement.category." + category) : category;
    }
}
