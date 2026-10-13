import { log } from "../infra/log";
import { notifyFriendRequest } from "../services/push_triggers";

/**
 * The client's own friend request (Nakama's AddFriends API) gets a push to the recipient;
 * Nakama already stores the in-app notice. `friend_add_by_code` calls the same helper.
 */
export function afterAddFriends(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  _data: unknown,
  request: nkruntime.AddFriendsRequest,
): void {
  const fromUserId = ctx.userId ?? "";
  if (fromUserId === "") return;
  const ids = [...(request.ids ?? [])];
  const usernames = request.usernames ?? [];
  if (usernames.length > 0) {
    try {
      for (const user of nk.usersGetUsername([...usernames])) ids.push(user.userId);
    } catch (error: unknown) {
      log.warn(logger, "friend request usernames not resolved", { error: String(error) });
    }
  }
  const now = Date.now();
  for (const toUserId of ids) {
    try {
      notifyFriendRequest(nk, logger, ctx.env, fromUserId, toUserId, now);
    } catch (error: unknown) {
      log.warn(logger, "friend request push not sent", { error: String(error) });
    }
  }
}
