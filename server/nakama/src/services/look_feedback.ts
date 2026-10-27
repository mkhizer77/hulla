/**
 * Playtest feedback on the test looks: one answer per player and look, the latest replacing the
 * earlier one, kept server-only and summarised for the owner without user ids or names.
 */
export const LOOK_FEEDBACK_COLLECTION = "look_feedback";

/** The looks under test; add an id here to collect feedback on it. */
export const FEEDBACK_LOOKS = ["basant", "pop"] as const;

export type FeedbackLook = (typeof FEEDBACK_LOOKS)[number];

export const COMMENT_MAX = 280;
export const ROUNDS_MAX = 999;
export const SUMMARY_COMMENTS_MAX = 200;

export interface LookFeedback {
  readonly look: FeedbackLook;
  readonly rating: number;
  readonly keep: boolean;
  readonly comment?: string;
  readonly rounds?: number;
  readonly at: number;
}

export type ParsedFeedback =
  | { readonly ok: true; readonly feedback: Omit<LookFeedback, "at"> }
  | { readonly ok: false; readonly reason: string };

export const isFeedbackLook = (value: unknown): value is FeedbackLook =>
  typeof value === "string" && (FEEDBACK_LOOKS as readonly string[]).includes(value);

const isControl = (ch: string): boolean => {
  const code = ch.codePointAt(0) ?? 0;
  return code < 0x20 || (code >= 0x7f && code <= 0x9f);
};

/** Trimmed with control characters removed; `null` when longer than the limit. */
export function cleanComment(value: string): string | null {
  const chars = Array.from(value).filter((ch) => !isControl(ch));
  const text = chars.join("").trim();
  return Array.from(text).length > COMMENT_MAX ? null : text;
}

export function parseFeedback(input: Record<string, unknown>): ParsedFeedback {
  const { look, rating, keep, comment, rounds } = input;
  if (!isFeedbackLook(look)) return { ok: false, reason: "look" };
  if (typeof rating !== "number" || !Number.isInteger(rating) || rating < 1 || rating > 5) {
    return { ok: false, reason: "rating" };
  }
  if (typeof keep !== "boolean") return { ok: false, reason: "keep" };
  let feedback: Omit<LookFeedback, "at"> = { look, rating, keep };
  if (comment !== undefined) {
    if (typeof comment !== "string") return { ok: false, reason: "comment" };
    const text = cleanComment(comment);
    if (text === null) return { ok: false, reason: "comment" };
    if (text !== "") feedback = { ...feedback, comment: text };
  }
  if (rounds !== undefined) {
    if (typeof rounds !== "number" || !Number.isInteger(rounds) || rounds < 0) {
      return { ok: false, reason: "rounds" };
    }
    feedback = { ...feedback, rounds: Math.min(rounds, ROUNDS_MAX) };
  }
  return { ok: true, feedback };
}

function isStored(value: unknown): value is LookFeedback {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["look"] === "string" &&
    typeof v["rating"] === "number" &&
    typeof v["keep"] === "boolean" &&
    typeof v["at"] === "number" &&
    (v["comment"] === undefined || typeof v["comment"] === "string")
  );
}

export interface FeedbackComment {
  readonly comment: string;
  readonly rating: number;
  readonly keep: boolean;
  readonly at: number;
}

export interface LookSummary {
  readonly count: number;
  /** One decimal; 0 when nobody answered. */
  readonly averageRating: number;
  /** The share of answers that want to keep the look, 0 to 1 with two decimals. */
  readonly keepShare: number;
  /** Newest first, at most `SUMMARY_COMMENTS_MAX`. */
  readonly comments: readonly FeedbackComment[];
}

const round = (value: number, places: number): number => {
  const f = 10 ** places;
  return Math.round(value * f) / f;
};

export function summarise(answers: readonly LookFeedback[]): Record<string, LookSummary> {
  const byLook = new Map<string, LookFeedback[]>(FEEDBACK_LOOKS.map((look) => [look, []]));
  for (const answer of answers) {
    const list = byLook.get(answer.look) ?? [];
    list.push(answer);
    byLook.set(answer.look, list);
  }
  const out: Record<string, LookSummary> = {};
  for (const [look, list] of byLook) {
    const count = list.length;
    const ratings = list.reduce((sum, a) => sum + a.rating, 0);
    const keeps = list.filter((a) => a.keep).length;
    const comments = list
      .filter((a) => a.comment !== undefined && a.comment !== "")
      .sort((a, b) => b.at - a.at)
      .slice(0, SUMMARY_COMMENTS_MAX)
      .map((a) => ({ comment: a.comment ?? "", rating: a.rating, keep: a.keep, at: a.at }));
    out[look] = {
      count,
      averageRating: count === 0 ? 0 : round(ratings / count, 1),
      keepShare: count === 0 ? 0 : round(keeps / count, 2),
      comments,
    };
  }
  return out;
}

export interface LookFeedbackStore {
  write(userId: string, feedback: LookFeedback): void;
  /** Every stored answer, across all players. */
  listAll(): LookFeedback[];
}

const PAGE = 100;
const PAGES_MAX = 100;

export function nkLookFeedbackStore(nk: nkruntime.Nakama): LookFeedbackStore {
  return {
    write(userId, feedback) {
      nk.storageWrite([
        {
          collection: LOOK_FEEDBACK_COLLECTION,
          key: feedback.look,
          userId,
          value: { ...feedback },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    listAll() {
      const out: LookFeedback[] = [];
      const seen = new Set<string>();
      let cursor: string | undefined;
      for (let page = 0; page < PAGES_MAX; page += 1) {
        // No owner: every player's answers.
        const list = nk.storageList(undefined, LOOK_FEEDBACK_COLLECTION, PAGE, cursor);
        const objects = list.objects ?? [];
        for (const object of objects) {
          if (object.collection !== LOOK_FEEDBACK_COLLECTION) continue;
          const id = `${object.userId}/${object.key}`;
          if (seen.has(id)) continue;
          seen.add(id);
          if (isStored(object.value) && object.value.look === object.key) out.push(object.value);
        }
        // Nakama may hand back a cursor on the last page; a short page or a repeated cursor ends it.
        if (
          objects.length < PAGE ||
          list.cursor === undefined ||
          list.cursor === "" ||
          list.cursor === cursor
        ) {
          break;
        }
        cursor = list.cursor;
      }
      return out;
    },
  };
}
