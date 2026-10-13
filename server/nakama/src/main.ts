import {
  thullaMatchInit,
  thullaMatchJoin,
  thullaMatchJoinAttempt,
  thullaMatchLeave,
  thullaMatchLoop,
  thullaMatchSignal,
  thullaMatchTerminate,
} from "./games/thulla/match";
import { afterAddFriends } from "./hooks/after_add_friends";
import {
  afterAuthenticateApple,
  afterAuthenticateCustom,
  afterAuthenticateDevice,
  afterAuthenticateFacebook,
  afterAuthenticateGoogle,
} from "./hooks/after_authenticate";
import {
  afterValidatePurchaseApple,
  afterValidatePurchaseGoogle,
} from "./hooks/after_validate_purchase";
import { beforeAuthenticateDevice } from "./hooks/before_authenticate";
import { beforeChannelMessageSend } from "./hooks/before_channel_message";
import { beforeMatchmakerAdd } from "./hooks/before_matchmaker_add";
import { matchmakerMatched } from "./hooks/matchmaker_matched";
import { log } from "./infra/log";
import {
  rpcAccountDelete,
  rpcAccountDeleteCancel,
  rpcAccountExport,
  rpcAdminPurgeDeleted,
} from "./rpc/account";
import { rpcAccountMerge, rpcAccountMergePrepare } from "./rpc/account_merge";
import { rpcAchievementsGet, rpcAchievementsSet } from "./rpc/achievements";
import {
  rpcAdminAuditList,
  rpcAdminGrant,
  rpcAdminRoundReplay,
  rpcAdminSanction,
  rpcAdminTableInspect,
  rpcSanctionsGet,
} from "./rpc/admin";
import { rpcAdReward, rpcAdsGet } from "./rpc/ads";
import { rpcChatFiltersGet, rpcChatFiltersSet } from "./rpc/chat_filters";
import { rpcConfigGet, rpcConfigSet } from "./rpc/config";
import { rpcContentGet, rpcContentManifest } from "./rpc/content";
import { rpcDailyRewardClaim, rpcDailyRewardGet } from "./rpc/daily_reward";
import { rpcProgressGet, rpcWalletGet } from "./rpc/economy";
import { rpcHello } from "./rpc/hello";
import { onLeaderboardReset, rpcLeaderboardGet } from "./rpc/leaderboards";
import {
  rpcMissionsClaim,
  rpcMissionsGet,
  rpcMissionsReroll,
  rpcMissionsSet,
} from "./rpc/missions";
import {
  rpcAdminChatLogsPurge,
  rpcBlockSet,
  rpcBlocksGet,
  rpcReportCreate,
  rpcReportsList,
} from "./rpc/moderation";
import { rpcAdminDrain } from "./rpc/ops";
import { rpcPassClaim, rpcPassGet, rpcPassPremiumGrant } from "./rpc/pass";
import { rpcPresenceSet, rpcTableListFriends } from "./rpc/presence";
import { rpcPresetsGet } from "./rpc/presets_get";
import { rpcProfileGet, rpcProfileUpdate } from "./rpc/profile";
import { rpcEconomyProducts, rpcIapRestore, rpcIapVerify } from "./rpc/purchases";
import {
  rpcAdminPushSweep,
  rpcAdminPushTest,
  rpcPushRegister,
  rpcPushSettingsGet,
  rpcPushSettingsSet,
  rpcPushUnregister,
  rpcTableInvite,
} from "./rpc/push";
import {
  rpcCatalogSet,
  rpcEconomyCatalog,
  rpcEconomyPurchase,
  rpcInventoryEquip,
} from "./rpc/shop";
import {
  rpcFavouriteSet,
  rpcFriendAddByCode,
  rpcMuteSet,
  rpcMutesGet,
  rpcSocialOverview,
} from "./rpc/social";
import { rpcTableCreate } from "./rpc/table_create";
import { rpcTableJoinByCode } from "./rpc/table_join_by_code";
import { rpcVoiceToken } from "./rpc/voice_token";
import { ensureLeaderboards } from "./services/leaderboards";

/**
 * Nakama calls this once at start-up; every RPC, hook and match handler registers here. Handlers
 * are passed by identifier and names are string literals: Nakama reads both out of this
 * function's source and looks the functions up on the global object.
 */
function InitModule(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  initializer: nkruntime.Initializer,
): void {
  initializer.registerRpc("hello", rpcHello);
  initializer.registerRpc("table_create", rpcTableCreate);
  initializer.registerRpc("table_join_by_code", rpcTableJoinByCode);
  initializer.registerRpc("presets_get", rpcPresetsGet);
  initializer.registerRpc("voice_token", rpcVoiceToken);
  initializer.registerRtBefore("ChannelMessageSend", beforeChannelMessageSend);
  initializer.registerRtBefore("MatchmakerAdd", beforeMatchmakerAdd);
  initializer.registerMatchmakerMatched(matchmakerMatched);
  initializer.registerRpc("profile_get", rpcProfileGet);
  initializer.registerRpc("profile_update", rpcProfileUpdate);
  initializer.registerRpc("account_delete", rpcAccountDelete);
  initializer.registerRpc("account_delete_cancel", rpcAccountDeleteCancel);
  initializer.registerRpc("account_export", rpcAccountExport);
  initializer.registerRpc("admin_purge_deleted", rpcAdminPurgeDeleted);
  initializer.registerRpc("account_merge_prepare", rpcAccountMergePrepare);
  initializer.registerRpc("account_merge", rpcAccountMerge);
  initializer.registerRpc("friend_add_by_code", rpcFriendAddByCode);
  initializer.registerRpc("favourite_set", rpcFavouriteSet);
  initializer.registerRpc("social_overview", rpcSocialOverview);
  initializer.registerRpc("table_list_friends", rpcTableListFriends);
  initializer.registerRpc("presence_set", rpcPresenceSet);
  initializer.registerRpc("mute_set", rpcMuteSet);
  initializer.registerRpc("mutes_get", rpcMutesGet);
  initializer.registerRpc("chat_filters_get", rpcChatFiltersGet);
  initializer.registerRpc("chat_filters_set", rpcChatFiltersSet);
  initializer.registerRpc("block_set", rpcBlockSet);
  initializer.registerRpc("blocks_get", rpcBlocksGet);
  initializer.registerRpc("report_create", rpcReportCreate);
  initializer.registerRpc("reports_list", rpcReportsList);
  initializer.registerRpc("admin_chat_logs_purge", rpcAdminChatLogsPurge);
  initializer.registerRpc("admin_table_inspect", rpcAdminTableInspect);
  initializer.registerRpc("admin_round_replay", rpcAdminRoundReplay);
  initializer.registerRpc("admin_sanction", rpcAdminSanction);
  initializer.registerRpc("admin_audit_list", rpcAdminAuditList);
  initializer.registerRpc("sanctions_get", rpcSanctionsGet);
  initializer.registerRpc("wallet_get", rpcWalletGet);
  initializer.registerRpc("progress_get", rpcProgressGet);
  initializer.registerRpc("missions_get", rpcMissionsGet);
  initializer.registerRpc("missions_claim", rpcMissionsClaim);
  initializer.registerRpc("missions_reroll", rpcMissionsReroll);
  initializer.registerRpc("missions_set", rpcMissionsSet);
  initializer.registerRpc("daily_reward_get", rpcDailyRewardGet);
  initializer.registerRpc("daily_reward_claim", rpcDailyRewardClaim);
  initializer.registerRpc("leaderboard_get", rpcLeaderboardGet);
  initializer.registerRpc("achievements_get", rpcAchievementsGet);
  initializer.registerRpc("achievements_set", rpcAchievementsSet);
  initializer.registerRpc("content_manifest", rpcContentManifest);
  initializer.registerRpc("content_get", rpcContentGet);
  initializer.registerRpc("config_get", rpcConfigGet);
  initializer.registerRpc("config_set", rpcConfigSet);
  initializer.registerRpc("economy_catalog", rpcEconomyCatalog);
  initializer.registerRpc("economy_purchase", rpcEconomyPurchase);
  initializer.registerRpc("inventory_equip", rpcInventoryEquip);
  initializer.registerRpc("catalog_set", rpcCatalogSet);
  initializer.registerRpc("admin_grant", rpcAdminGrant);
  initializer.registerRpc("pass_get", rpcPassGet);
  initializer.registerRpc("pass_claim", rpcPassClaim);
  initializer.registerRpc("pass_premium_grant", rpcPassPremiumGrant);
  initializer.registerRpc("admin_drain", rpcAdminDrain);
  initializer.registerRpc("push_register", rpcPushRegister);
  initializer.registerRpc("push_unregister", rpcPushUnregister);
  initializer.registerRpc("push_settings_get", rpcPushSettingsGet);
  initializer.registerRpc("push_settings_set", rpcPushSettingsSet);
  initializer.registerRpc("table_invite", rpcTableInvite);
  initializer.registerRpc("admin_push_sweep", rpcAdminPushSweep);
  initializer.registerRpc("admin_push_test", rpcAdminPushTest);
  initializer.registerAfterAddFriends(afterAddFriends);
  initializer.registerRpc("economy_products", rpcEconomyProducts);
  initializer.registerRpc("iap_verify", rpcIapVerify);
  initializer.registerRpc("iap_restore", rpcIapRestore);
  initializer.registerRpc("ad_reward", rpcAdReward);
  initializer.registerRpc("ads_get", rpcAdsGet);
  initializer.registerAfterValidatePurchaseApple(afterValidatePurchaseApple);
  initializer.registerAfterValidatePurchaseGoogle(afterValidatePurchaseGoogle);
  initializer.registerLeaderboardReset(onLeaderboardReset);
  ensureLeaderboards(nk, logger);
  initializer.registerBeforeAuthenticateDevice(beforeAuthenticateDevice);
  initializer.registerAfterAuthenticateDevice(afterAuthenticateDevice);
  initializer.registerAfterAuthenticateApple(afterAuthenticateApple);
  initializer.registerAfterAuthenticateGoogle(afterAuthenticateGoogle);
  initializer.registerAfterAuthenticateFacebook(afterAuthenticateFacebook);
  initializer.registerAfterAuthenticateCustom(afterAuthenticateCustom);
  initializer.registerMatch("thulla", {
    matchInit: thullaMatchInit,
    matchJoinAttempt: thullaMatchJoinAttempt,
    matchJoin: thullaMatchJoin,
    matchLeave: thullaMatchLeave,
    matchLoop: thullaMatchLoop,
    matchTerminate: thullaMatchTerminate,
    matchSignal: thullaMatchSignal,
  });
  log.info(logger, "Hulla runtime loaded", { env: ctx.env["HULLA_ENV"] ?? "local" });
}

// Nakama looks InitModule up on the global object. The explicit assignment keeps the bundler
// from dropping the function and lets the tests load it the same way Nakama does.
(globalThis as unknown as { InitModule: nkruntime.InitModule }).InitModule = InitModule;
