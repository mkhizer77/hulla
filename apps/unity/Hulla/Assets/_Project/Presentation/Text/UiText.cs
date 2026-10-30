using Hulla.Application.Social;

namespace Hulla.Presentation.Text
{
    /// <summary>
    /// The UI's text by key from the string tables (GDD 26.1, ADR-0023); the English lives in
    /// content/strings/en.json. Properties return patterns: format the ones with arguments through
    /// <see cref="Strings.Format"/>.
    /// </summary>
    public static class UiText
    {
        public static string DailyBackTomorrow => Strings.Text("ui.daily_back_tomorrow");

        public static string PlusCoins => Strings.Text("ui.plus_coins");

        public static string CodeHint => Strings.Text("ui.code_hint");

        public static string PasteCode => Strings.Text("ui.paste_code");

        public static string SeatsShort => Strings.Text("ui.seats_short");

        public static string SeatedOfThree => Strings.Text("ui.seated_of_three");

        public static string WaitingReady => Strings.Text("ui.waiting_ready");

        public static string ChatPlaceholder => Strings.Text("ui.chat_placeholder");

        public static string TurnFollow => Strings.Text("ui.turn_follow");

        public static string TurnLead => Strings.Text("ui.turn_lead");

        public static string TurnAny => Strings.Text("ui.turn_any");

        public static string WaitingForSeat => Strings.Text("ui.waiting_for_seat");

        public static string WatchingPill => Strings.Text("ui.watching_pill");

        public static string LeaveTableTitle => Strings.Text("ui.leave_table_title");

        public static string LeaveTableBody => Strings.Text("ui.leave_table_body");

        public static string Stay => Strings.Text("ui.stay");

        public static string Score => Strings.Text("ui.score");

        public static string ScoreTitle => Strings.Text("ui.score_title");

        public static string ScoreBhabhi => Strings.Text("ui.score_bhabhi");

        public static string ScorePlayer => Strings.Text("ui.score_player");

        public static string SeedLater => Strings.Text("ui.seed_later");

        public static string WatchingSheet => Strings.Text("ui.watching_sheet");

        public static string WatchingFull => Strings.Text("ui.watching_full");

        public static string SeatedNext => Strings.Text("ui.seated_next");

        public static string Phrases => Strings.Text("ui.phrases");

        public static string Emotes => Strings.Text("ui.emotes");

        public static string LeaveTable => Strings.Text("ui.leave_table");

        public static string LevelN => Strings.Text("ui.level_n");

        public static string FairDealShort => Strings.Text("ui.fair_deal_short");

        public static string NewHere => Strings.Text("ui.new_here");

        public static string AddNamePlus => Strings.Text("ui.add_name_plus");

        public static string SearchFriends => Strings.Text("ui.search_friends");

        public static string PlayingNow => Strings.Text("ui.playing_now");

        public static string EveryoneElse => Strings.Text("ui.everyone_else");

        public static string AtTableSeats => Strings.Text("ui.at_table_seats");

        public static string InARound => Strings.Text("ui.in_a_round");

        public static string OnHome => Strings.Text("ui.on_home");

        public static string Watch => Strings.Text("ui.watch");

        public static string JoinTheirTable => Strings.Text("ui.join_their_table");

        public static string InviteToMine => Strings.Text("ui.invite_to_mine");

        public static string RemoveFriend => Strings.Text("ui.remove_friend");

        public static string TheirCode => Strings.Text("ui.their_code");

        public static string YourFriendCode => Strings.Text("ui.your_friend_code");

        public static string FriendsEmpty => Strings.Text("ui.friends_empty");

        public static string AddFriendTitle => Strings.Text("ui.add_friend_title");

        public static string FavouriteWord => Strings.Text("ui.favourite_word");

        public static string UnfavouriteWord => Strings.Text("ui.unfavourite_word");

        public static string WhereTable => Strings.Text("ui.where_table");

        public static string WhereWatching => Strings.Text("ui.where_watching");

        public static string MissionsTodayResets => Strings.Text("ui.missions_today_resets");

        public static string MissionsWeekLeft => Strings.Text("ui.missions_week_left");

        public static string SwapOne => Strings.Text("ui.swap_one");

        public static string Swapped => Strings.Text("ui.swapped");

        public static string Swap => Strings.Text("ui.swap");

        public static string ClaimCoins => Strings.Text("ui.claim_coins");

        public static string OfCount => Strings.Text("ui.of_count");

        public static string PlusReward => Strings.Text("ui.plus_reward");

        public static string PlusRewardGems => Strings.Text("ui.plus_reward_gems");

        public static string UnlockedOn => Strings.Text("ui.unlocked_on");

        public static string AchievementReward => Strings.Text("ui.achievement_reward");

        public static string PointsToClimb => Strings.Text("ui.points_to_climb");

        public static string PlayRankedShort => Strings.Text("ui.play_ranked");

        public static string SpotlightDays => Strings.Text("ui.spotlight_days");

        public static string OwnedEquip => Strings.Text("ui.owned_equip");

        public static string BuyEquip => Strings.Text("ui.buy_equip");

        public static string YouHave => Strings.Text("ui.you_have");

        public static string CoinsShort => Strings.Text("ui.coins_short");

        public static string ItemTypeRarity => Strings.Text("ui.item_type_rarity");

        public static string GemsOnlyBalance => Strings.Text("ui.gems_balance");

        public static string DaysLeft => Strings.Text("ui.days_left");

        public static string TierNext => Strings.Text("ui.tier_next");

        public static string XpOf => Strings.Text("ui.xp_of");

        public static string FreeTrack => Strings.Text("ui.free_track");

        public static string PremiumTrack => Strings.Text("ui.premium_track");

        public static string PremiumStore => Strings.Text("ui.premium_store");

        public static string SettingsMore => Strings.Text("ui.settings_more");

        public static string PrivacyBlocked => Strings.Text("ui.privacy_blocked");

        public static string AccountData => Strings.Text("ui.account_data");

        public static string HelpHow => Strings.Text("ui.help_how");

        public static string NotifySummary => Strings.Text("ui.notify_summary");

        public static string PhoneLinked => Strings.Text("ui.phone_linked");

        public static string HowToPlayRow => Strings.Text("ui.how_to_play_row");

        public static string RulebookRow => Strings.Text("ui.rulebook_row");

        public static string BlockedPlayers => Strings.Text("ui.blocked_players");

        public static string NoBlocked => Strings.Text("ui.no_blocked");

        public static string VersionFooter => Strings.Text("ui.version_footer");

        public static string Language => Strings.Text("ui.language");

        public static string SuitShort => Strings.Text("ui.suit_short");

        public static string RankShort => Strings.Text("ui.rank_short");

        public static string ProfileLine => Strings.Text("ui.profile_line");

        public static string ProfileLineTitle => Strings.Text("ui.profile_line_title");

        public static string StatRounds => Strings.Text("ui.stat_rounds");

        public static string StatEscapes => Strings.Text("ui.stat_escapes");

        public static string StatThullas => Strings.Text("ui.stat_thullas");

        public static string StatBhabhi => Strings.Text("ui.stat_bhabhi");

        public static string AchievementsOf => Strings.Text("ui.achievements_of");

        public static string SeeAll => Strings.Text("ui.see_all");

        public static string RankedLine => Strings.Text("ui.ranked_line");

        public static string EditProfile => Strings.Text("ui.edit_profile");

        public static string HandleRule => Strings.Text("ui.handle_rule");

        public static string LeagueYou => Strings.Text("ui.league_you");

        public static string PhonePlaceholder => Strings.Text("ui.phone_placeholder");

        public static string YourName => Strings.Text("ui.your_name");

        public static string ChangeAnyTime => Strings.Text("ui.change_any_time");

        public static string AnotherName => Strings.Text("ui.another_name");

        public static string TypeMyOwn => Strings.Text("ui.type_my_own");

        public static string Continue => Strings.Text("ui.continue");

        public static string HowToPlayStep => Strings.Text("ui.how_to_play_step");

        public static string AlsoBlock => Strings.Text("ui.also_block");

        public static string SendReport => Strings.Text("ui.send_report");

        public static string InviteGroup => Strings.Text("ui.invite_group");

        public static string ShareLink => Strings.Text("ui.share_link");

        public static string SentTick => Strings.Text("ui.sent_tick");

        public static string SecondsShort => Strings.Text("ui.seconds_short");

        public static string LobbyHintHostReady => Strings.Text("ui.lobby_hint_host_ready");

        public static string ScoreBhabhiCards => Strings.Text("ui.score_bhabhi_cards");

        public static string ScoreValue => Strings.Text("ui.score_value");

        public static string FairRoundBody => Strings.Text("ui.fair_round_body");

        public static string CopyCommitment => Strings.Text("ui.copy_commitment");

        public static string FairHow => Strings.Text("ui.fair_how");

        public static string FairHowTitle => Strings.Text("ui.fair_how_title");

        public static string FairHowBody => Strings.Text("ui.fair_how_body");

        public static string FairEvery => Strings.Text("ui.fair_every");

        public static string PlayerStats => Strings.Text("ui.player_stats");

        public static string Voice => Strings.Text("ui.voice");

        public static string PlayerLine => Strings.Text("ui.player_line");

        public static string PlayerLineTitle => Strings.Text("ui.player_line_title");

        public static string Dot => Strings.Text("ui.dot");

        public static string WatchingCount => Strings.Text("ui.watching_count");

        public static string WatchingNoSeats => Strings.Text("ui.watching_no_seats");

        public static string WatchingTapRow => Strings.Text("ui.watching_tap_row");

        public static string AppTitle => Strings.Text("ui.app_title");
        public static string Tagline => Strings.Text("ui.tagline");
        public static string NameLabel => Strings.Text("ui.name_label");
        public static string NameTooShort => Strings.Text("ui.name_too_short");
        public static string CreateTable => Strings.Text("ui.create_table");
        public static string CodeLabel => Strings.Text("ui.code_label");
        public static string JoinTable => Strings.Text("ui.join_table");
        public static string Connecting => Strings.Text("ui.connecting");
        public static string RoomCode => Strings.Text("ui.room_code");
        public static string Ready => Strings.Text("ui.ready");
        public static string NotReady => Strings.Text("ui.not_ready");
        public static string Start => Strings.Text("ui.start");
        public static string Leave => Strings.Text("ui.leave");
        public static string Host => Strings.Text("ui.host");
        public static string Waiting => Strings.Text("ui.waiting");
        public static string Empty => Strings.Text("ui.empty");
        public static string TakeHand => Strings.Text("ui.take_hand");
        public static string AskNamed => Strings.Text("ui.ask_named");
        public static string AskedWaiting => Strings.Text("ui.asked_waiting");
        public static string AsksForHand => Strings.Text("ui.asks_for_hand");
        public static string KeepsCards => Strings.Text("ui.keeps_cards");
        public static string AskSheetTitle => Strings.Text("ui.ask_sheet_title");
        public static string AskSheetBody => Strings.Text("ui.ask_sheet_body");
        public static string HandOver => Strings.Text("ui.hand_over");
        public static string KeepPlaying => Strings.Text("ui.keep_playing");

        public static string BotPlaysFor => Strings.Text("ui.bot_plays_for");

        public static string PlayerBack => Strings.Text("ui.player_back");

        public static string BackAtTable => Strings.Text("ui.back_at_table");
        public static string YouAreOut => Strings.Text("ui.you_are_out");
        public static string OutWatching => Strings.Text("ui.out_watching");
        public static string Escaped => Strings.Text("ui.escaped");
        public static string Away => Strings.Text("ui.away");

        /// <summary>A seat whose player left or lost connection; the server plays it for the rest of the round.</summary>
        public static string SeatAwayAutoPlay => Strings.Text("ui.seat_away_auto_play");

        /// <summary>A present player whose turns time out, so the server plays for them (R45).</summary>
        public static string SeatAutoPlay => Strings.Text("ui.seat_auto_play");

        /// <summary>Whose turn it is when it is not the player's own.</summary>
        public static string RejoinTable => Strings.Text("ui.rejoin_table");

        public static string RejoinGame => Strings.Text("ui.rejoin_game");

        public static string LeftTheTable => Strings.Text("ui.left_the_table");

        public static string InviteSomeone => Strings.Text("ui.invite_someone");

        public static string ContinueWithout => Strings.Text("ui.continue_without");

        public static string NeedThreeInvite => Strings.Text("ui.need_three_invite");

        public static string NeedThreeFindTable => Strings.Text("ui.need_three_find_table");
        public static string Thulla => Strings.Text("ui.thulla");
        public static string PickedUp => Strings.Text("ui.picked_up");
        public static string Bhabhi => Strings.Text("ui.bhabhi");
        public static string LoserIs => Strings.Text("ui.loser_is");
        public static string AfterRounds => Strings.Text("ui.after_rounds");
        public static string PointsLine => Strings.Text("ui.points_line");
        public static string Highlights => Strings.Text("ui.highlights");
        public static string MostThullas => Strings.Text("ui.most_thullas");
        public static string MostPickedUp => Strings.Text("ui.most_picked_up");
        public static string MakeFriends => Strings.Text("ui.make_friends");
        public static string AddName => Strings.Text("ui.add_name");
        public static string RematchVotes => Strings.Text("ui.rematch_votes");
        public static string EscapeOrder => Strings.Text("ui.escape_order");
        public static string PlayAgain => Strings.Text("ui.play_again");
        public static string RematchHow => Strings.Text("ui.rematch_how");
        public static string AppName => Strings.Text("ui.app_name");
        public static string ShareCardGame => Strings.Text("ui.share_card_game");
        public static string ShareResult => Strings.Text("ui.share_result");
        public static string ShareAs => Strings.Text("ui.share_as");
        public static string SharePost => Strings.Text("ui.share_post");
        public static string ShareStory => Strings.Text("ui.share_story");
        public static string ShareResultTitle => Strings.Text("ui.share_result_title");

        /// <summary>{0} loser's name, {1} the table's loser title, {2} room code, {3} the invite link.</summary>
        public static string ShareResultWithCode => Strings.Text("ui.share_result_with_code");

        /// <summary>{0} loser's name, {1} the table's loser title, {2} the referral link.</summary>
        public static string ShareResultReferral => Strings.Text("ui.share_result_referral");

        /// <summary>{0} loser's name, {1} the table's loser title.</summary>
        public static string ShareResultText => Strings.Text("ui.share_result_text");

        public static string ShareFooterCode => Strings.Text("ui.share_footer_code");
        public static string ShareFooter => Strings.Text("ui.share_footer");

        /// <summary>{0} the sharer's friend code.</summary>
        public static string ShareFooterFriend => Strings.Text("ui.share_footer_friend");

        /// <summary>{0} name, {1} cards picked up.</summary>
        public static string ShareBigThulla => Strings.Text("ui.share_big_thulla");

        public static string NotNow => Strings.Text("ui.not_now");
        public static string Waiting4Rematch => Strings.Text("ui.waiting4_rematch");
        public static string You => Strings.Text("ui.you");
        public static string Reconnecting => Strings.Text("ui.reconnecting");
        public static string Chat => Strings.Text("ui.chat");
        public static string Send => Strings.Text("ui.send");
        public static string ChatTooFast => Strings.Text("ui.chat_too_fast");
        public static string ChatGuest => Strings.Text("ui.chat_guest");
        public static string ChatFiltered => Strings.Text("ui.chat_filtered");
        public static string Mute => Strings.Text("ui.mute");

        public static string MicOn => Strings.Text("ui.voice.mic_on");

        public static string MicOff => Strings.Text("ui.voice.mic_off");

        public static string VoiceRetry => Strings.Text("ui.voice.retry");

        public static string HoldToTalk => Strings.Text("ui.voice.hold_to_talk");

        public static string Talking => Strings.Text("ui.voice.talking");

        /// <summary>The one line beside the microphone prompt (GDD 11.4).</summary>
        public static string VoiceWhy => Strings.Text("ui.voice.why");

        public static string VoiceWaiting => Strings.Text("ui.voice.waiting");

        public static string VoiceJoining => Strings.Text("ui.voice.joining");

        public static string VoicePaused => Strings.Text("ui.voice.paused");

        public static string VoiceFailed => Strings.Text("ui.voice.failed");

        public static string VoiceSuspended => Strings.Text("ui.voice.suspended");

        public static string VoiceNoPermission => Strings.Text("ui.voice.no_permission");

        public static string VoiceListening => Strings.Text("ui.voice.listening");

        /// <summary>The speaker toggle while voices are heard.</summary>
        public static string VoicesOn => Strings.Text("ui.voice.speaker_on");

        /// <summary>The speaker toggle while voices are off.</summary>
        public static string VoicesOff => Strings.Text("ui.voice.speaker_off");

        public static string VoiceChatLabel => Strings.Text("ui.voice_chat_label");

        public static string VoiceVolumeLabel => Strings.Text("ui.voice_volume_label");
        public static string Report => Strings.Text("ui.report");
        public static string ReportWhy => Strings.Text("ui.report_why");
        public static string ReportThanks => Strings.Text("ui.report_thanks");
        public static string Blocked => Strings.Text("ui.blocked");

        /// <summary>The reasons of GDD 10.11, in the server's order (services/reports REPORT_REASONS).</summary>
        public static (string Id, string Label)[] ReportReasons => new[]
        {
            ("abusive_text", Strings.Text("report.reason.abusive_text")),
            ("abusive_voice", Strings.Text("report.reason.abusive_voice")),
            ("cheating", Strings.Text("report.reason.cheating")),
            ("spam", Strings.Text("report.reason.spam")),
            ("inappropriate_profile", Strings.Text("report.reason.inappropriate_profile")),
            ("underage", Strings.Text("report.reason.underage")),
        };
        public static string Unmute => Strings.Text("ui.unmute");
        public static string MutedNote => Strings.Text("ui.muted_note");

        public static string ChatProblem(string code) => code switch
        {
            "network.rate_limited" => ChatTooFast,
            "auth.not_linked" => ChatGuest,
            "content.filtered" => ChatFiltered,
            "social.muted" => Strings.Text("chat.problem.social_muted"),
            _ => ChatRefused,
        };

        public static string ChatRefused => Strings.Text("ui.chat_refused");
        public static string QuickPlay => Strings.Text("ui.quick_play");
        public static string Settings => Strings.Text("ui.settings");
        public static string SettingsAccount => Strings.Text("ui.settings_account");
        public static string SettingsPrivacy => Strings.Text("ui.settings_privacy");
        public static string AnalyticsLabel => Strings.Text("ui.analytics_label");
        public static string AnalyticsAsk => Strings.Text("ui.analytics_ask");
        public static string AnalyticsYes => Strings.Text("ui.analytics_yes");
        public static string AnalyticsNo => Strings.Text("ui.analytics_no");
        public static string ExportData => Strings.Text("ui.export_data");
        public static string ExportShared => Strings.Text("ui.export_shared");
        public static string ExportCopied => Strings.Text("ui.export_copied");
        public static string DeleteAccount => Strings.Text("ui.delete_account");
        public static string DeleteConfirm => Strings.Text("ui.delete_confirm");
        public static string DeleteYes => Strings.Text("ui.delete_yes");
        public static string DeleteNo => Strings.Text("ui.delete_no");
        public static string DeletePending => Strings.Text("ui.delete_pending");
        public static string CancelDeletion => Strings.Text("ui.cancel_deletion");
        public static string DeletionCancelled => Strings.Text("ui.deletion_cancelled");
        public static string Done => Strings.Text("ui.done");
        public static string SettingsPlay => Strings.Text("ui.settings_play");
        public static string SettingsSeeing => Strings.Text("ui.settings_seeing");
        public static string SettingsTheme => Strings.Text("ui.settings_theme");
        public static string GameThulla => Strings.Text("ui.game_thulla");
        public static string GameThullaLine => Strings.Text("ui.game_thulla_line");
        public static string GameRung => Strings.Text("ui.game_rung");
        public static string GameRungLine => Strings.Text("ui.game_rung_line");
        public static string Trophies => Strings.Text("nav.trophies");
        public static string ComingSoon => Strings.Text("ui.coming_soon");

        /// <summary>The table themes in settings order (docs/design/table-themes.md).</summary>
        public static string SkinName(Hulla.Application.Settings.TableSkin skin) => skin switch
        {
            Hulla.Application.Settings.TableSkin.Haveli => Strings.Text("skin.haveli"),
            Hulla.Application.Settings.TableSkin.Dhaba => Strings.Text("skin.dhaba"),
            Hulla.Application.Settings.TableSkin.Mehndi => Strings.Text("skin.mehndi"),
            Hulla.Application.Settings.TableSkin.Adda => Strings.Text("skin.adda"),
            Hulla.Application.Settings.TableSkin.Basant => Strings.Text("skin.basant"),
            Hulla.Application.Settings.TableSkin.Pop => Strings.Text("skin.pop"),
            _ => Strings.Text("skin.club"),
        };
        public static string OneTapPlay => Strings.Text("ui.one_tap_play");
        public static string HandSortLabel => Strings.Text("ui.hand_sort_label");
        public static string SortBySuit => Strings.Text("ui.sort_by_suit");
        public static string SortByRank => Strings.Text("ui.sort_by_rank");
        public static string HapticsLabel => Strings.Text("ui.haptics_label");
        public static string PlayNow => Strings.Text("ui.play_now");
        public static string CreateRoom => Strings.Text("ui.create_room");
        public static string JoinByCode => Strings.Text("ui.join_by_code");
        public static string Retry => Strings.Text("ui.retry");
        public static string Loading => Strings.Text("ui.loading");
        public static string ChangeRules => Strings.Text("ui.change_rules");
        public static string FairDealCheck => Strings.Text("ui.fair_deal_check");
        public static string FairDealExplained => Strings.Text("ui.fair_deal_explained");
        public static string CopySeed => Strings.Text("ui.copy_seed");
        public static string StartWith => Strings.Text("ui.start_with");
        public static string NoMissionsYet => Strings.Text("ui.no_missions_yet");
        public static string NoAchievementsYet => Strings.Text("ui.no_achievements_yet");
        public static string NothingThisWeek => Strings.Text("ui.nothing_this_week");
        public static string ShowMyCode => Strings.Text("ui.show_my_code");
        public static string InviteYourGroup => Strings.Text("ui.invite_your_group");
        public static string PartyEmpty => Strings.Text("ui.party_empty");
        public static string Watching1 => Strings.Text("ui.watching1");

        /// <summary>The bottom navigation (GDD 14.1).</summary>
        public static string NavLabel(Navigation.NavTab tab) => tab switch
        {
            Navigation.NavTab.Home => Strings.Text("nav.home"),
            Navigation.NavTab.Friends => Friends,
            Navigation.NavTab.Trophies => Trophies,
            _ => Shop,
        };

        public static string SettingsDevelopment => Strings.Text("ui.settings_development");
        public static string SettingsSound => Strings.Text("ui.settings_sound");
        public static string SfxLabel => Strings.Text("ui.sfx_label");

        /// <summary>The stamp for a pile of five or more (GDD 14.6).</summary>
        public static string ThullaBig => Strings.Text("ui.thulla_big");

        /// <summary>The short flag on the seat that gave the thulla (GDD 14.5).</summary>
        public static string ThullaFlag => Strings.Text("ui.thulla_flag");

        /// <summary>The leader's crown (GDD 14.5), as a glyph until the icon set lands.</summary>
        public static string Crown => Strings.Text("ui.crown");

        /// <summary>Opens the full quick-chat panel from the strip.</summary>
        public static string More => Strings.Text("ui.more");

        /// <summary>The picked-up tray in the player sheet (GDD 14.7).</summary>
        public static string PickedUpTray => Strings.Text("ui.picked_up_tray");
        public static string RulesRecapLabel => Strings.Text("ui.rules_recap_label");
        public static string FourColourLabel => Strings.Text("ui.four_colour_label");
        public static string HighContrastLabel => Strings.Text("ui.high_contrast_label");
        public static string LargeCardsLabel => Strings.Text("ui.large_cards_label");
        public static string TextSizeLabel => Strings.Text("ui.text_size_label");
        public static string ReduceMotionLabel => Strings.Text("ui.reduce_motion_label");
        public static string LeftHandedLabel => Strings.Text("ui.left_handed_label");
        public static string Playing => Strings.Text("ui.playing");
        public static string Lead => Strings.Text("ui.lead");
        public static string DiscardPile => Strings.Text("ui.discard_pile");
        public static string FairDeal => Strings.Text("ui.fair_deal");
        public static string MustFollow => Strings.Text("ui.must_follow");
        public static string LeadAceOfSpades => Strings.Text("ui.lead_ace_of_spades");
        public static string NotYourTurn => Strings.Text("ui.not_your_turn");
        public static string NotPlayableNow => Strings.Text("ui.not_playable_now");
        public static string SeatsLabel => Strings.Text("ui.seats_label");
        public static string Cancel => Strings.Text("ui.cancel");
        public static string Searching => Strings.Text("ui.searching");
        public static string SearchingWider => Strings.Text("ui.searching_wider");
        public static string SearchingStart => Strings.Text("ui.searching_start");
        public static string FoundTable => Strings.Text("ui.found_table");
        public static string SearchingBots => Strings.Text("ui.searching_bots");
        public static string BotTag => Strings.Text("ui.bot_tag");
        public static string StartsIn => Strings.Text("ui.starts_in");
        public static string QuickPlayTable => Strings.Text("ui.quick_play_table");
        public static string RecapTitle => Strings.Text("ui.recap_title");
        public static string Recap => Strings.Text("ui.recap");
        public static string RecapHide => Strings.Text("ui.recap_hide");
        public static string Close => Strings.Text("ui.close");

        /// <summary>Emote ids read as words until the emote art arrives (#34): "laugh_cry" → "Laugh cry".</summary>
        public static string EmoteLabel(string id) => Strings.Has("emote." + id) ? Strings.Text("emote." + id) : Words(id);
        public static string Server => Strings.Text("ui.server");

        public static string Profile => Strings.Text("ui.profile");
        public static string Missions => Strings.Text("ui.missions");
        public static string Leaderboard => Strings.Text("ui.leaderboard");
        public static string Achievements => Strings.Text("ui.achievements");
        public static string Shop => Strings.Text("ui.shop");
        public static string InviteFriends => Strings.Text("ui.invite_friends");
        public static string Invite => Strings.Text("ui.invite");
        public static string Invited => Strings.Text("ui.invited");
        public static string AtTable => Strings.Text("ui.at_table");
        public static string NoFriendsToInvite => Strings.Text("ui.no_friends_to_invite");

        /// <summary>{0} inviter's name, {1} room code.</summary>
        public static string InviteReceived => Strings.Text("ui.invite_received");
        public static string InviteLinkWhileSeated => Strings.Text("ui.invite_link_while_seated");
        public static string ReferralWhileSeated => Strings.Text("ui.referral_while_seated");

        /// <summary>{0} the sharer's friend code.</summary>
        public static string ReferralAsk => Strings.Text("ui.referral_ask");

        public static string Notifications => Strings.Text("ui.notifications");
        public static string NotifyInvites => Strings.Text("ui.notify_invites");
        public static string NotifyFriendRequests => Strings.Text("ui.notify_friend_requests");
        public static string NotifyFriendOnline => Strings.Text("ui.notify_friend_online");
        public static string NotifyDailyReward => Strings.Text("ui.notify_daily_reward");
        public static string NotifySeasonEnd => Strings.Text("ui.notify_season_end");
        public static string NotifyQuietHours => Strings.Text("ui.notify_quiet_hours");
        public static string NotifyNonFriendInvites => Strings.Text("ui.notify_non_friend_invites");

        /// <summary>Why an invite didn't go: a table_invite skip reason or an error code.</summary>
        public static string InviteProblem(string reason) => reason switch
        {
            "blocked" => Strings.Text("invite.problem.blocked"),
            "muted" => Strings.Text("invite.problem.muted"),
            "rate_limited" or "network.rate_limited" => Strings.Text("invite.problem.rate_limited"),
            "self" => Strings.Text("invite.problem.self"),
            _ => Strings.Text("invite.problem.other"),
        };
        public static string SeasonPass => Strings.Text("ui.season_pass");
        public static string NoSeason => Strings.Text("ui.no_season");

        /// <summary>{0} tier, {1} tier count, {2} XP into the tier, {3} XP per tier.</summary>
        public static string PassProgress => Strings.Text("ui.pass_progress");

        /// <summary>{0} tier count.</summary>
        public static string PassComplete => Strings.Text("ui.pass_complete");

        /// <summary>{0} time left, such as "3d 4h".</summary>
        public static string SeasonEndsIn => Strings.Text("ui.season_ends_in");

        public static string DoubleXp => Strings.Text("ui.double_xp");
        public static string PremiumLocked => Strings.Text("ui.premium_locked");
        public static string Locked => Strings.Text("ui.locked");

        /// <summary>{0} the title's name.</summary>
        public static string TitleReward => Strings.Text("ui.title_reward");
        public static string Spotlight => Strings.Text("ui.spotlight");
        public static string Equip => Strings.Text("ui.equip");
        public static string Equipped => Strings.Text("ui.equipped");
        public static string Free => Strings.Text("ui.free");
        public static string PriceCoins => Strings.Text("ui.price_coins");
        public static string PriceGems => Strings.Text("ui.price_gems");

        /// <summary>{0} coins, {1} gems.</summary>
        public static string Balances => Strings.Text("ui.balances");

        /// <summary>{0} item name, {1} price.</summary>
        public static string BuyQuestion => Strings.Text("ui.buy_question");

        public static string Buy => Strings.Text("ui.buy");

        /// <summary>{0} unlocked, {1} total.</summary>
        public static string AchievementsCount => Strings.Text("ui.achievements_count");

        public static string GemsOnly => Strings.Text("ui.gems_only");

        /// <summary>{0} gems, {1} the title's name.</summary>
        public static string GemsAndTitle => Strings.Text("ui.gems_and_title");

        /// <summary>{0} the achievement's name.</summary>
        public static string AchievementUnlocked => Strings.Text("ui.achievement_unlocked");

        /// <summary>A title key ("thulla_baaz") as words ("Thulla baaz") until the string tables land (#65).</summary>
        public static string TitleName(string key) => Strings.Has("title." + key) ? Strings.Text("title." + key) : Words(key);

        /// <summary>An id as words, for anything the tables don't name yet: "laugh_cry" → "Laugh cry".</summary>
        public static string Words(string id)
        {
            var words = id.Replace('_', ' ');
            return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
        public static string ThisWeek => Strings.Text("ui.this_week");
        public static string ThisMonth => Strings.Text("ui.this_month");
        public static string Everyone => Strings.Text("ui.everyone");
        public static string FriendsTab => Strings.Text("ui.friends_tab");
        public static string BoardEmpty => Strings.Text("ui.board_empty");

        /// <summary>{0} time left, such as "3d 4h".</summary>
        public static string ResetsIn => Strings.Text("ui.resets_in");

        public static string PointsShort => Strings.Text("ui.points_short");

        /// <summary>{0} the player's own name on a board.</summary>
        public static string YouAre => Strings.Text("ui.you_are");
        public static string MissionsToday => Strings.Text("ui.missions_today");
        public static string MissionsWeek => Strings.Text("ui.missions_week");
        public static string MissionProgress => Strings.Text("ui.mission_progress");
        public static string RewardCoins => Strings.Text("ui.reward_coins");
        public static string RewardGems => Strings.Text("ui.reward_gems");
        public static string Claim => Strings.Text("ui.claim");

        /// <summary>{0} day of the 7-day calendar, {1} the amount.</summary>
        public static string DailyReady => Strings.Text("ui.daily_ready");

        public static string DailyClaimed => Strings.Text("ui.daily_claimed");
        public static string DailyTomorrow => Strings.Text("ui.daily_tomorrow");
        public static string CoinsOnly => Strings.Text("ui.coins_only");
        public static string CoinsAndGems => Strings.Text("ui.coins_and_gems");
        public static string Claimed => Strings.Text("ui.claimed");
        public static string Reroll => Strings.Text("ui.reroll");

        /// <summary>A mission described from what it counts (server services/missions), until the string tables (#65).</summary>
        public static string MissionText(string metric, long target) =>
            Strings.Has("mission.goal." + metric)
                ? Strings.Get("mission.goal." + metric, target)
                : Strings.Get("mission.goal.other", metric, target);
        public static string ProgressLine => Strings.Text("ui.progress_line");
        public static string StreakSuffix => Strings.Text("ui.streak_suffix");
        public static string RewardLine => Strings.Text("ui.reward_line");
        public static string RewardCapped => Strings.Text("ui.reward_capped");
        public static string LevelUp => Strings.Text("ui.level_up");

        /// <summary>Starting names for a new player (GDD 17.2), changeable on Home.</summary>
        public static string[] NamePlaces => Names("name.place.", 8);

        public static string[] NameAnimals => Names("name.animal.", 8);

        private static string[] Names(string prefix, int count)
        {
            var names = new string[count];
            for (var i = 0; i < count; i++)
            {
                names[i] = Strings.Text(prefix + i);
            }

            return names;
        }
        public static string EditRules => Strings.Text("ui.edit_rules");
        public static string PresetLabel => Strings.Text("ui.preset_label");
        public static string TurnTimerLabel => Strings.Text("ui.turn_timer_label");
        public static string LoserTitleLabel => Strings.Text("ui.loser_title_label");
        public static string RulesSummary => Strings.Text("ui.rules_summary");
        public static string RulesSummaryLastCard => Strings.Text("ui.rules_summary_last_card");
        public static string EmptyHandLabel => Strings.Text("ui.empty_hand_label");
        public static string EmptyHandDrawDiscard => Strings.Text("ui.empty_hand_draw_discard");
        public static string EmptyHandDrawNext => Strings.Text("ui.empty_hand_draw_next");
        public static string EmptyHandInstantExit => Strings.Text("ui.empty_hand_instant_exit");
        public static string RulesSummaryHand => Strings.Text("ui.rules_summary_hand");
        public static string HandTakingLabel => Strings.Text("ui.hand_taking_label");
        public static string HandTakingLeader => Strings.Text("ui.hand_taking_leader");
        public static string HandTakingOff => Strings.Text("ui.hand_taking_off");
        public static string HandTakingAnyone => Strings.Text("ui.hand_taking_anyone");
        public static string TurnSeconds => Strings.Text("ui.turn_seconds");
        public static string NoTimer => Strings.Text("ui.no_timer");
        public static string RulesChangedBy => Strings.Text("ui.rules_changed_by");
        public static string JoinFriendTable => Strings.Text("ui.join_friend_table");
        public static string WatchFriendTable => Strings.Text("ui.watch_friend_table");
        public static string FriendTableBanner => Strings.Text("ui.friend_table_banner");
        public static string FriendsOnline => Strings.Text("ui.friends_online");
        public static string SeatSize => Strings.Text("ui.seat_size");
        public static string Casual => Strings.Text("ui.casual");
        public static string Ranked => Strings.Text("ui.ranked");
        public static string DailyReward => Strings.Text("ui.daily_reward");
        public static string DailyDay => Strings.Text("ui.daily_day");
        public static string Watching => Strings.Text("ui.watching");
        public static string WatchingLobby => Strings.Text("ui.watching_lobby");
        public static string Watchers => Strings.Text("ui.watchers");
        public static string SeatThem => Strings.Text("ui.seat_them");
        public static string SeatsNext => Strings.Text("ui.seats_next");

        public static string Presence(PresenceState state) => state switch
        {
            PresenceState.Lobby => Strings.Text("presence.lobby"),
            PresenceState.Round => Strings.Text("presence.round"),
            PresenceState.Away => Strings.Text("presence.away"),
            _ => Strings.Text("presence.online"),
        };
        public static string ShareCode => Strings.Text("ui.share_code");
        public static string ShareTitle => Strings.Text("ui.share_title");

        /// <summary>The bilingual invite (GDD 10.5) with the code and the link that opens the app at the table.</summary>
        public static string ShareText => Strings.Text("ui.share_text");

        public static string ShareFriendText => Strings.Text("ui.share_friend_text");
        public static string Friends => Strings.Text("ui.friends");
        public static string Back => Strings.Text("ui.back");
        public static string Save => Strings.Text("ui.save");
        public static string Saved => Strings.Text("ui.saved");
        public static string Copy => Strings.Text("ui.copy");
        public static string Copied => Strings.Text("ui.copied");
        public static string EmotePackLocked => Strings.Text("ui.emote_pack_locked");
        public static string Share => Strings.Text("ui.share");
        public static string SearchElapsed => Strings.Text("ui.search_elapsed");
        public static string PrivateTable => Strings.Text("ui.private_table");
        public static string LobbyHint => Strings.Text("ui.lobby_hint");
        public static string LobbyHintHost => Strings.Text("ui.lobby_hint_host");
        public static string DisplayNameLabel => Strings.Text("ui.display_name_label");
        public static string HandleLabel => Strings.Text("ui.handle_label");
        public static string BioLabel => Strings.Text("ui.bio_label");
        public static string FriendCodeLabel => Strings.Text("ui.friend_code_label");
        public static string StatsLine => Strings.Text("ui.stats_line");
        public static string AddByCode => Strings.Text("ui.add_by_code");
        public static string Add => Strings.Text("ui.add");
        public static string RequestSent => Strings.Text("ui.request_sent");
        public static string Requests => Strings.Text("ui.requests");
        public static string SentRequests => Strings.Text("ui.sent_requests");
        public static string FriendsList => Strings.Text("ui.friends_list");
        public static string Recent => Strings.Text("ui.recent");
        public static string BlockedList => Strings.Text("ui.blocked_list");
        public static string Accept => Strings.Text("ui.accept");
        public static string Decline => Strings.Text("ui.decline");
        public static string CancelRequest => Strings.Text("ui.cancel_request");
        public static string Remove => Strings.Text("ui.remove");
        public static string Block => Strings.Text("ui.block");
        public static string Unblock => Strings.Text("ui.unblock");
        public static string AddFriend => Strings.Text("ui.add_friend");
        public static string Favourite => Strings.Text("ui.favourite");
        public static string NotFavourite => Strings.Text("ui.not_favourite");
        public static string Online => Strings.Text("ui.online");
        public static string Offline => Strings.Text("ui.offline");
        public static string RoundsTogether => Strings.Text("ui.rounds_together");
        public static string NoFriendsYet => Strings.Text("ui.no_friends_yet");

        /// <summary>Plain words for the server's error codes; anything else falls back to the code.</summary>
        public static string Error(string code) =>
            Strings.Has("error." + code) ? Strings.Text("error." + code) : Strings.Get("error.unknown", code);
    }
}
