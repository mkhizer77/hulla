using Hulla.Application.Settings;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// The look playtest (art-design/concepts, concepts 2 and 4): Home offers Basant and Pop, and back
    /// on Home after a round in one of them the player is asked once what they think of it. The
    /// answer goes to the server (look_feedback). Remote config's looks_test switch turns it off.
    /// </summary>
    public sealed class LookPlaytest
    {
        public const string BeforeKey = "hulla.looks.before";

        public static readonly TableSkin[] Looks = { TableSkin.Basant, TableSkin.Pop };

        private readonly ISettingsStore _store;
        private bool _snoozed;

        public LookPlaytest(ISettingsStore store)
        {
            _store = store;
        }

        public static bool IsTestLook(TableSkin skin) => skin == TableSkin.Basant || skin == TableSkin.Pop;

        /// <summary>The id the server knows the look by: basant, pop.</summary>
        public static string Id(TableSkin skin) => skin.ToString().ToLowerInvariant();

        /// <summary>The look the player had before trying a test look; the way back if they do not keep it.</summary>
        public TableSkin Before
        {
            get
            {
                var stored = _store.GetInt(BeforeKey, (int)TableSkin.Club);
                return System.Enum.IsDefined(typeof(TableSkin), stored) && !IsTestLook((TableSkin)stored) ? (TableSkin)stored : TableSkin.Club;
            }
        }

        public void Switched(TableSkin from, TableSkin to)
        {
            if (from != to && !IsTestLook(from) && IsTestLook(to))
            {
                _store.SetInt(BeforeKey, (int)from);
                _store.Save();
            }
        }

        public void RoundFinished(TableSkin skin)
        {
            if (IsTestLook(skin))
            {
                _store.SetInt(RoundsKey(skin), Rounds(skin) + 1);
                _store.Save();
            }
        }

        public int Rounds(TableSkin skin) => _store.GetInt(RoundsKey(skin), 0);

        public bool Answered(TableSkin skin) => _store.GetInt(AnsweredKey(skin), 0) == 1;

        /// <summary>Ask now: a test look with a round played in it, not answered and not put off this session.</summary>
        public bool Due(TableSkin skin, bool enabled) => enabled && !_snoozed && IsTestLook(skin) && !Answered(skin) && Rounds(skin) > 0;

        /// <summary>Not now: ask again next time the app starts.</summary>
        public void Snooze() => _snoozed = true;

        public void MarkAnswered(TableSkin skin)
        {
            _store.SetInt(AnsweredKey(skin), 1);
            _store.Save();
        }

        /// <summary>The Home card stays until both looks have an answer.</summary>
        public bool ShowCard(bool enabled)
        {
            if (!enabled)
            {
                return false;
            }

            foreach (var look in Looks)
            {
                if (!Answered(look))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RoundsKey(TableSkin skin) => "hulla.looks.rounds." + Id(skin);

        private static string AnsweredKey(TableSkin skin) => "hulla.looks.answered." + Id(skin);
    }
}
