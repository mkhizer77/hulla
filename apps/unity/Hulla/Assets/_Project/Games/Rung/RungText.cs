using Hulla.Presentation.Text;

namespace Hulla.Games.Rung
{
    /// <summary>The Rung table's text by key; the English lives in content/strings/en.json.</summary>
    public static class RungText
    {
        public static string Choose => Strings.Text("rung.choose");
        public static string Choosing => Strings.Text("rung.choosing");
        public static string Redeal => Strings.Text("rung.redeal");
        public static string Redealt => Strings.Text("rung.redealt");
        public static string Us => Strings.Text("rung.us");
        public static string Them => Strings.Text("rung.them");
        public static string Tricks => Strings.Text("rung.tricks");
        public static string Pile => Strings.Text("rung.pile");
        public static string OneMore => Strings.Text("rung.one_more");
        public static string PileTaken => Strings.Text("rung.pile_taken");
        public static string CourtTitle => Strings.Text("rung.court_title");
        public static string CourtStop => Strings.Text("rung.court_stop");
        public static string CourtPlayOn => Strings.Text("rung.court_play_on");
        public static string Chasing => Strings.Text("rung.chasing");
        public static string DealWon => Strings.Text("rung.deal_won");
        public static string DealLost => Strings.Text("rung.deal_lost");
        public static string DealSeen => Strings.Text("rung.deal_seen");
        public static string Court => Strings.Text("rung.court");
        public static string Court52 => Strings.Text("rung.court52");
        public static string BaziWon => Strings.Text("rung.bazi_won");
        public static string BaziLost => Strings.Text("rung.bazi_lost");
        public static string Bazi => Strings.Text("rung.bazi");
        public static string NextDeal => Strings.Text("rung.next_deal");
        public static string NextDealVotes => Strings.Text("rung.next_deal_votes");
        public static string NewBazi => Strings.Text("rung.new_bazi");
        public static string Partner => Strings.Text("rung.partner");
        public static string YourTurn => Strings.Text("rung.your_turn");
        public static string TurnOf => Strings.Text("rung.turn_of");
        public static string SwitchTo => Strings.Text("rung.switch_to");
        public static string SwitchQueued => Strings.Text("rung.switch_queued");
        public static string Switched => Strings.Text("rung.switched");
        public static string AddBot => Strings.Text("rung.add_bot");
        public static string RemoveBot => Strings.Text("rung.remove_bot");
        public static string NeedFour => Strings.Text("rung.need_four");
        /// <summary>The lobby's rules line for a Rung room: the rule set's name and its one line.</summary>
        public static string Summary(Hulla.Protocol.Preset? preset)
        {
            var id = preset == Hulla.Protocol.Preset.CourtPiece ? "courtPiece" : preset == Hulla.Protocol.Preset.Hokm ? "hokm" : "rung";
            return Strings.Text("preset." + id + ".name") + " · " + Strings.Text("preset." + id + ".description");
        }

        public static string RuleSet => Strings.Text("rung.rule_set");
        public static string PlayTo => Strings.Text("rung.play_to");
        public static string PointsN => Strings.Text("rung.points_n");
        public static string Kaat => Strings.Text("rung.kaat");
        public static string CourtStamp => Strings.Text("rung.court_stamp");
        public static string WonTrick => Strings.Text("rung.won_trick");
        public static string Called => Strings.Text("rung.called");
        public static string ScoreTitle => Strings.Text("rung.score_title");
        public static string ScoreLine => Strings.Text("rung.score_line");

        public static string SuitName(char suit) => suit switch
        {
            'H' => Strings.Text("rung.suit_H"),
            'C' => Strings.Text("rung.suit_C"),
            'D' => Strings.Text("rung.suit_D"),
            _ => Strings.Text("rung.suit_S"),
        };

        public static string PlayRung => Strings.Text("rung.play_rung");
    }
}
