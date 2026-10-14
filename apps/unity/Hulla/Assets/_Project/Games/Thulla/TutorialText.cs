using Hulla.Presentation.Text;

namespace Hulla.Games.Thulla
{
    /// <summary>The coach's lines (GDD 17.3), in one place for the string tables (#65).</summary>
    public static class TutorialText
    {
        public static string Gate => Strings.Text("tutorial.gate");
        public static string GateYes => Strings.Text("tutorial.gate_yes");
        public static string GateNo => Strings.Text("tutorial.gate_no");
        public static string Refresher => Strings.Text("tutorial.refresher");
        public static string HowToPlay => Strings.Text("tutorial.how_to_play");
        public static string Next => Strings.Text("tutorial.next");
        public static string Finish => Strings.Text("tutorial.finish");
        public static string Skip => Strings.Text("tutorial.skip");

        public static string GotIt => Strings.Text("tutorial.got_it");
        public static string HintLegal => Strings.Text("tutorial.hint_legal");
        public static string HintLeader => Strings.Text("tutorial.hint_leader");
        public static string HintTakeHand => Strings.Text("tutorial.hint_take_hand");

        /// <summary>Shown once, the first time a player taps Ask for hand: {0} the player, {1} their card count.</summary>
        public static string ExplainAsk => Strings.Text("tutorial.explain_ask");
        public static string HintQuickChat => Strings.Text("tutorial.hint_quick_chat");

        public static string Step1 => Strings.Text("tutorial.step1");
        public static string Step1Hint => Strings.Text("tutorial.step1_hint");
        public static string Step1Done => Strings.Text("tutorial.step1_done");

        public static string Step2Lead => Strings.Text("tutorial.step2_lead");
        public static string Step2Hint => Strings.Text("tutorial.step2_hint");
        public static string Step2 => Strings.Text("tutorial.step2");
        public static string Step2Done => Strings.Text("tutorial.step2_done");

        public static string Step3 => Strings.Text("tutorial.step3");
        public static string Step3Hint => Strings.Text("tutorial.step3_hint");
        public static string Step3Done => Strings.Text("tutorial.step3_done");

        public static string Step4 => Strings.Text("tutorial.step4");
        public static string Step4Hint => Strings.Text("tutorial.step4_hint");
        public static string Step4Done => Strings.Text("tutorial.step4_done");

        public static string Step5Lead => Strings.Text("tutorial.step5_lead");
        public static string Step5Hint => Strings.Text("tutorial.step5_hint");
        public static string Step5 => Strings.Text("tutorial.step5");
        public static string Step5Done => Strings.Text("tutorial.step5_done");
    }
}
