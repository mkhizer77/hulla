/**
 * The error-code catalogue (ARCHITECTURE 12.5). Codes are `area.name`; the user-facing text is the
 * localisation key derived from the code, never prose. Adding a code adds a localisation key and a
 * test (CODING_RULES 12).
 */
export const ErrorCodes = {
  rules: {
    illegalMove: "rules.illegal_move",
    wrongPhase: "rules.wrong_phase",
    notYourTurn: "rules.not_your_turn",
    seatNotActive: "rules.seat_not_active",
    cardNotInHand: "rules.card_not_in_hand",
    mustFollowSuit: "rules.must_follow_suit",
    mustLeadAceOfSpades: "rules.must_lead_ace_of_spades",
    handTakeDisabled: "rules.hand_take_disabled",
    handTakeDisabledEndgame: "rules.hand_take_disabled_endgame",
    handTakeNotLeader: "rules.hand_take_not_leader",
    handTakeWindowClosed: "rules.hand_take_window_closed",
    roundOver: "rules.round_over",
    invalidRules: "rules.invalid_rules",
    invalidSeatCount: "rules.invalid_seat_count",
    invalidSeat: "rules.invalid_seat",
  },
  table: {
    full: "table.full",
    locked: "table.locked",
    banned: "table.banned",
    blocked: "table.blocked",
    notFound: "table.not_found",
    invalidCode: "table.invalid_code",
    notHost: "table.not_host",
    notInRound: "table.not_in_round",
    staleSequence: "table.stale_sequence",
    duplicateMove: "table.duplicate_move",
    unsupportedVersion: "table.unsupported_version",
    invalidPayload: "table.invalid_payload",
    notAuthorised: "table.not_authorised",
    draining: "table.draining",
    notSeated: "table.not_seated",
  },
  auth: {
    sessionExpired: "auth.session_expired",
    notLinked: "auth.not_linked",
    deviceLimit: "auth.device_limit",
    mergeCodeInvalid: "auth.merge_code_invalid",
    banned: "auth.banned",
    updateRequired: "auth.update_required",
  },
  economy: {
    insufficientFunds: "economy.insufficient_funds",
    receiptInvalid: "economy.receipt_invalid",
    storeUnavailable: "economy.store_unavailable",
    adRewardInvalid: "economy.ad_reward_invalid",
    adsUnavailable: "economy.ads_unavailable",
    alreadyGranted: "economy.already_granted",
    dailyCapReached: "economy.daily_cap_reached",
    itemUnknown: "economy.item_unknown",
    itemOwned: "economy.item_owned",
  },
  content: {
    manifestUnsigned: "content.manifest_unsigned",
    manifestDowngrade: "content.manifest_downgrade",
    packUnknown: "content.pack_unknown",
    schemaInvalid: "content.schema_invalid",
    filtered: "content.filtered",
  },
  network: {
    disconnected: "network.disconnected",
    timeout: "network.timeout",
    rateLimited: "network.rate_limited",
  },
  voice: {
    unavailable: "voice.unavailable",
    notAtTable: "voice.not_at_table",
    suspended: "voice.suspended",
  },
  profile: {
    invalidName: "profile.invalid_name",
    invalidHandle: "profile.invalid_handle",
    handleTaken: "profile.handle_taken",
    nameChangeLocked: "profile.name_change_locked",
    notVisible: "profile.not_visible",
    unknownAvatar: "profile.unknown_avatar",
    notFound: "profile.not_found",
  },
  social: {
    self: "social.self",
    friendCap: "social.friend_cap",
    favouriteCap: "social.favourite_cap",
    notFriends: "social.not_friends",
    blocked: "social.blocked",
    muted: "social.muted",
  },
} as const;

type Leaves<T> = T extends string ? T : { [K in keyof T]: Leaves<T[K]> }[keyof T];

export type ErrorCode = Leaves<typeof ErrorCodes>;

export type ErrorArea = keyof typeof ErrorCodes;

/** Structured detail is limited to primitives so it serialises and logs safely (no hands, no PII). */
export interface RuleError {
  readonly code: ErrorCode;
  readonly detail?: Readonly<Record<string, string | number | boolean>>;
}

export const ALL_ERROR_CODES: readonly ErrorCode[] = Object.values(ErrorCodes).flatMap((group) =>
  Object.values(group),
);

export function ruleError(
  code: ErrorCode,
  detail?: Readonly<Record<string, string | number | boolean>>,
): RuleError {
  return detail === undefined ? { code } : { code, detail };
}

export function errorArea(code: ErrorCode): ErrorArea {
  return code.slice(0, code.indexOf(".")) as ErrorArea;
}

/** The string-table key for a code: `error.rules.illegal_move` and so on (ARCHITECTURE 12.5). */
export function localisationKey(code: ErrorCode): string {
  return `error.${code}`;
}
