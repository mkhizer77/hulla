import {
  thullaMatchInit,
  thullaMatchJoin,
  thullaMatchJoinAttempt,
  thullaMatchLeave,
  thullaMatchLoop,
  thullaMatchSignal,
  thullaMatchTerminate,
} from "./games/thulla/match";
import {
  afterAuthenticateApple,
  afterAuthenticateCustom,
  afterAuthenticateDevice,
  afterAuthenticateFacebook,
  afterAuthenticateGoogle,
} from "./hooks/after_authenticate";
import { beforeChannelMessageSend } from "./hooks/before_channel_message";
import { beforeMatchmakerAdd } from "./hooks/before_matchmaker_add";
import { matchmakerMatched } from "./hooks/matchmaker_matched";
import {
  rpcAccountDelete,
  rpcAccountDeleteCancel,
  rpcAccountExport,
  rpcAdminPurgeDeleted,
} from "./rpc/account";
import { rpcAccountMerge, rpcAccountMergePrepare } from "./rpc/account_merge";
import { rpcChatFiltersGet, rpcChatFiltersSet } from "./rpc/chat_filters";
import { rpcHello } from "./rpc/hello";
import {
  rpcAdminChatLogsPurge,
  rpcBlockSet,
  rpcBlocksGet,
  rpcReportCreate,
  rpcReportsList,
} from "./rpc/moderation";
import { rpcPresenceSet, rpcTableListFriends } from "./rpc/presence";
import { rpcPresetsGet } from "./rpc/presets_get";
import { rpcProfileGet, rpcProfileUpdate } from "./rpc/profile";
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

/**
 * Nakama calls this once at start-up; every RPC, hook and match handler registers here. Handlers
 * are passed by identifier and names are string literals: Nakama reads both out of this
 * function's source and looks the functions up on the global object.
 */
function InitModule(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
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
  logger.info("Hulla runtime loaded", { env: ctx.env["HULLA_ENV"] ?? "local" });
}

// Nakama looks InitModule up on the global object. The explicit assignment keeps the bundler
// from dropping the function and lets the tests load it the same way Nakama does.
(globalThis as unknown as { InitModule: nkruntime.InitModule }).InitModule = InitModule;
