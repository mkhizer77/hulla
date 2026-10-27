import { createSign } from "node:crypto";

import type { ServerLogger, ServerRuntime } from "../infra/nk";

export interface LogLine {
  readonly level: "debug" | "info" | "warn" | "error";
  readonly message: string;
  readonly fields: Record<string, unknown> | undefined;
}

/** A deterministic runtime for unit tests: fixed clock, counted uuids, remembered log lines. */
export function fakeRuntime(
  options: { now?: number } = {},
): ServerRuntime & { readonly lines: LogLine[] } {
  const lines: LogLine[] = [];
  let uuids = 0;
  const log =
    (level: LogLine["level"]) =>
    (message: string, fields?: Record<string, unknown>): void => {
      lines.push({ level, message, fields });
    };
  const logger: ServerLogger = {
    debug: log("debug"),
    info: log("info"),
    warn: log("warn"),
    error: log("error"),
  };
  return {
    uuidv4: () => {
      uuids += 1;
      return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
    },
    sha256Hex: (input) => `sha256(${input})`,
    nowMs: () => options.now ?? 1_760_200_000_000,
    logger,
    lines,
  };
}

/** The pieces of Nakama's Initializer the module registers against, recorded for assertions. */
export interface FakeInitializer {
  readonly rpcs: Map<string, nkruntime.RpcFunction>;
  readonly matches: Map<string, nkruntime.MatchHandler>;
  readonly hooks: Map<string, (...args: unknown[]) => unknown>;
  readonly initializer: nkruntime.Initializer;
}

export function fakeInitializer(): FakeInitializer {
  const rpcs = new Map<string, nkruntime.RpcFunction>();
  const matches = new Map<string, nkruntime.MatchHandler>();
  const hooks = new Map<string, (...args: unknown[]) => unknown>();
  const initializer = {
    registerMatchmakerMatched: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("matchmakerMatched", fn);
    },
    registerRpc: (id: string, fn: nkruntime.RpcFunction) => {
      rpcs.set(id, fn);
    },
    registerMatch: (name: string, handler: nkruntime.MatchHandler) => {
      matches.set(name, handler);
    },
    registerRtBefore: (id: string, fn: (...args: unknown[]) => unknown) => {
      hooks.set(`rtBefore:${id}`, fn);
    },
    registerBeforeAuthenticateDevice: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("beforeAuthenticateDevice", fn);
    },
    registerLeaderboardReset: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("leaderboardReset", fn);
    },
    registerAfterAuthenticateDevice: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAuthenticateDevice", fn);
    },
    registerAfterAuthenticateApple: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAuthenticateApple", fn);
    },
    registerAfterAuthenticateGoogle: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAuthenticateGoogle", fn);
    },
    registerAfterAuthenticateFacebook: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAuthenticateFacebook", fn);
    },
    registerBeforeAuthenticateCustom: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("beforeAuthenticateCustom", fn);
    },
    registerAfterAuthenticateCustom: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAuthenticateCustom", fn);
    },
    registerAfterAddFriends: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterAddFriends", fn);
    },
    registerAfterValidatePurchaseApple: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterValidatePurchaseApple", fn);
    },
    registerAfterValidatePurchaseGoogle: (fn: (...args: unknown[]) => unknown) => {
      hooks.set("afterValidatePurchaseGoogle", fn);
    },
  } as unknown as nkruntime.Initializer;
  return { rpcs, matches, hooks, initializer };
}

export function fakeNk(): nkruntime.Nakama {
  return {
    uuidv4: () => "11111111-2222-4333-8444-555555555555",
    sha256Hash: (input: string) => `sha256(${input})`,
    leaderboardCreate: () => undefined,
  } as unknown as nkruntime.Nakama;
}

export function fakeLogger(lines: LogLine[] = []): nkruntime.Logger {
  const push = (level: LogLine["level"]) => (message: string, fields?: Record<string, unknown>) => {
    lines.push({ level, message, fields });
  };
  return {
    debug: push("debug"),
    info: push("info"),
    warn: push("warn"),
    error: push("error"),
  } as unknown as nkruntime.Logger;
}

interface StoredObject {
  collection: string;
  key: string;
  userId: string;
  value: Record<string, unknown>;
  version: string;
  permissionRead: number;
  permissionWrite: number;
}

interface MemoryUser {
  userId: string;
  username: string;
  displayName: string;
  avatarUrl: string;
  email: string;
  customId: string;
  appleId: string;
  googleId: string;
  facebookId: string;
}

/** What an in-memory Nakama remembers, for assertions. */
export interface MemoryNk {
  readonly nk: nkruntime.Nakama;
  readonly storage: Map<string, StoredObject>;
  readonly users: Map<string, MemoryUser>;
  /** userId -> otherId -> friend state (0 friend, 1 sent, 2 received, 3 blocked). */
  readonly friends: Map<string, Map<string, number>>;
  readonly online: Set<string>;
  readonly matches: Map<string, { label: string; size: number; params: Record<string, unknown> }>;
  readonly wallets: Map<string, Record<string, number>>;
  readonly ledger: readonly {
    userId: string;
    changeset: Record<string, number>;
    metadata: Record<string, unknown>;
  }[];
  /** userId -> device ids, as `accountGetId` lists them. */
  readonly devices: Map<string, string[]>;
  readonly deleted: string[];
  readonly loggedOut: string[];
  readonly signals: { matchId: string; data: string }[];
  readonly notifications: {
    userId: string;
    subject: string;
    code: number;
    content: Record<string, unknown>;
    senderId: string | null;
  }[];
  /**
   * Purchases Nakama would hold after `purchaseValidateApple/Google` with persist. A fake receipt
   * is JSON `{productId, transactionId, refundTime?, environment?}`; "unconfigured" throws the
   * store's not-configured error, anything else that is not JSON is a tampered receipt.
   */
  readonly purchases: {
    userId: string;
    productId: string;
    transactionId: string;
    store: string;
    environment: string;
    purchaseTime: number;
    refundTime: number;
  }[];
  /** Every `httpRequest`, for assertions; `http` answers them (a 200 for Google by default). */
  readonly requests: {
    url: string;
    method: string;
    headers: Record<string, string>;
    body: string;
  }[];
  http: (
    url: string,
    method: string,
    headers: Record<string, string>,
    body: string,
  ) => { code: number; headers: string[]; body: string };
  /** leaderboard id -> owner -> record; `expiry` groups the periods. */
  readonly boards: Map<
    string,
    Map<
      string,
      { score: number; numScore: number; expiry: number; metadata: Record<string, unknown> }
    >
  >;
  user(id: string, overrides?: Partial<MemoryUser>): MemoryUser;
  object(collection: string, key: string, userId: string): Record<string, unknown> | undefined;
}

const storageKey = (collection: string, userId: string, key: string): string =>
  `${collection}/${userId}/${key}`;

/**
 * A Nakama that keeps everything in maps: storage with versions, users, the friend graph,
 * matches and wallets. Enough for the RPCs and hooks to run end to end in a unit test.
 */
export function memoryNk(): MemoryNk {
  const storage = new Map<string, StoredObject>();
  const users = new Map<string, MemoryUser>();
  const friends = new Map<string, Map<string, number>>();
  const online = new Set<string>();
  const matches = new Map<
    string,
    { label: string; size: number; params: Record<string, unknown> }
  >();
  const wallets = new Map<string, Record<string, number>>();
  const ledger: {
    id: string;
    userId: string;
    createTime: number;
    updateTime: number;
    changeset: Record<string, number>;
    metadata: Record<string, unknown>;
  }[] = [];
  const devices = new Map<string, string[]>();
  const deleted: string[] = [];
  const loggedOut: string[] = [];
  const signals: { matchId: string; data: string }[] = [];
  const notifications: MemoryNk["notifications"][number][] = [];
  const requests: MemoryNk["requests"][number][] = [];
  const purchases: MemoryNk["purchases"][number][] = [];
  const validate = (store: string, userId: string, receipt: string, persist: boolean) => {
    if (receipt === "unconfigured") throw new Error(`${store} IAP is not configured`);
    let parsed: {
      productId?: unknown;
      transactionId?: unknown;
      refundTime?: unknown;
      environment?: unknown;
    };
    try {
      parsed = JSON.parse(receipt) as typeof parsed;
    } catch {
      throw new Error("invalid receipt");
    }
    if (typeof parsed.productId !== "string" || typeof parsed.transactionId !== "string") {
      throw new Error("invalid receipt");
    }
    const existing = purchases.find((p) => p.transactionId === parsed.transactionId);
    const record = existing ?? {
      userId,
      productId: parsed.productId,
      transactionId: parsed.transactionId,
      store,
      environment: typeof parsed.environment === "string" ? parsed.environment : "SANDBOX",
      purchaseTime: 1,
      refundTime: typeof parsed.refundTime === "number" ? parsed.refundTime : 0,
    };
    if (existing === undefined && persist) purchases.push(record);
    return {
      validatedPurchases: [
        {
          ...record,
          createTime: 1,
          updateTime: 1,
          providerResponse: "{}",
          seenBefore: existing !== undefined,
        },
      ],
    };
  };
  const self: { http: MemoryNk["http"] } = {
    http: (url) => ({
      code: 200,
      headers: [],
      body:
        url === "https://oauth2.googleapis.com/token"
          ? JSON.stringify({ access_token: "at-1", expires_in: 3600 })
          : JSON.stringify({ name: "projects/test/messages/1" }),
    }),
  };
  const boards = new Map<
    string,
    Map<
      string,
      { score: number; numScore: number; expiry: number; metadata: Record<string, unknown> }
    >
  >();
  const boardMeta = new Map<string, { nextReset: number; operator: string }>();
  let uuids = 0;
  let versions = 0;
  const user = (id: string, overrides: Partial<MemoryUser> = {}): MemoryUser => {
    const existing = users.get(id) ?? {
      userId: id,
      username: `user_${id}`,
      displayName: "",
      avatarUrl: "",
      email: "",
      customId: "",
      appleId: "",
      googleId: "",
      facebookId: "",
    };
    const merged = { ...existing, ...overrides };
    users.set(id, merged);
    return merged;
  };
  const edges = (id: string): Map<string, number> => {
    const found = friends.get(id);
    if (found !== undefined) return found;
    const created = new Map<string, number>();
    friends.set(id, created);
    return created;
  };
  const toUser = (u: MemoryUser): nkruntime.User =>
    ({
      userId: u.userId,
      username: u.username,
      displayName: u.displayName,
      avatarUrl: u.avatarUrl,
      appleId: u.appleId,
      googleId: u.googleId,
      facebookId: u.facebookId,
      online: online.has(u.userId),
      createTime: 0,
      updateTime: 0,
      metadata: {},
    }) as unknown as nkruntime.User;
  const nk = {
    uuidv4: () => {
      uuids += 1;
      return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
    },
    sha256Hash: (input: string) => `sha256(${input})`,
    rsaSha256Hash: (input: string, key: string) =>
      createSign("RSA-SHA256").update(input).sign(key, "base64"),
    binaryToString: (data: ArrayBuffer) => new TextDecoder().decode(data),
    storageWrite(requests: nkruntime.StorageWriteRequest[]) {
      return requests.map((r) => {
        const k = storageKey(r.collection, r.userId ?? "", r.key);
        const existing = storage.get(k);
        if (r.version === "*" && existing !== undefined)
          throw new Error("storage version conflict");
        versions += 1;
        const object: StoredObject = {
          collection: r.collection,
          key: r.key,
          userId: r.userId ?? "",
          value: JSON.parse(JSON.stringify(r.value)) as Record<string, unknown>,
          version: `v${String(versions)}`,
          permissionRead: r.permissionRead ?? 1,
          permissionWrite: r.permissionWrite ?? 1,
        };
        storage.set(k, object);
        return {
          collection: r.collection,
          key: r.key,
          userId: r.userId ?? "",
          version: object.version,
        };
      });
    },
    storageRead(reads: nkruntime.StorageReadRequest[]) {
      return reads.flatMap((r) => {
        const found = storage.get(storageKey(r.collection, r.userId, r.key));
        return found === undefined ? [] : [{ ...found, createTime: 0, updateTime: 0 }];
      });
    },
    storageList(userId: string | undefined, collection: string, limit: number, cursor?: string) {
      const anyOwner = userId === undefined || userId === "";
      const all = [...storage.values()]
        .filter((o) => o.collection === collection && (anyOwner || o.userId === userId))
        .sort((a, b) => a.key.localeCompare(b.key));
      const start = cursor === undefined || cursor === "" ? 0 : Number(cursor);
      const page = all.slice(start, start + limit);
      const next = start + limit < all.length ? String(start + limit) : undefined;
      return { objects: page.map((o) => ({ ...o, createTime: 0, updateTime: 0 })), cursor: next };
    },
    storageDelete(deletes: nkruntime.StorageDeleteRequest[]) {
      for (const d of deletes) storage.delete(storageKey(d.collection, d.userId, d.key));
    },
    usersGetId: (ids: string[]) => ids.map((id) => toUser(user(id))),
    usersGetUsername: (names: string[]) =>
      [...users.values()].filter((u) => names.includes(u.username)).map(toUser),
    accountGetId(id: string) {
      const u = user(id);
      return {
        user: toUser(u),
        wallet: JSON.stringify(wallets.get(id) ?? {}),
        email: u.email,
        devices: (devices.get(id) ?? []).map((deviceId) => ({ id: deviceId })),
        customId: u.customId,
        verifyTime: 0,
        disableTime: 0,
        providers: [],
      };
    },
    accountUpdateId(id: string, username?: string | null, displayName?: string | null) {
      user(id, {
        ...(username === null || username === undefined ? {} : { username }),
        ...(displayName === null || displayName === undefined ? {} : { displayName }),
      });
    },
    linkCustom(id: string, customId: string) {
      const taken = [...users.values()].find((u) => u.customId === customId && u.userId !== id);
      if (taken !== undefined) throw new Error("custom id in use");
      if (user(id).customId !== "") throw new Error("account already has a custom id");
      user(id, { customId });
    },
    unlinkCustom(id: string, customId: string) {
      if (user(id).customId !== customId) throw new Error("not linked");
      user(id, { customId: "" });
    },
    authenticateCustom(customId: string, _username?: string, create = true) {
      const found = [...users.values()].find((u) => u.customId === customId);
      if (found === undefined) {
        if (!create) throw new Error("account not found");
        throw new Error("create is not supported in the fake");
      }
      return { userId: found.userId, username: found.username, created: false };
    },
    base64Encode: (text: string) => Buffer.from(text).toString("base64"),
    accountExportId: (id: string) => JSON.stringify({ account: { user: toUser(user(id)) } }),
    accountDeleteId(id: string) {
      deleted.push(id);
      devices.delete(id);
    },
    linkDevice(id: string, deviceId: string) {
      const owner = [...devices.entries()].find(([, ids]) => ids.includes(deviceId));
      if (owner !== undefined && owner[0] !== id) throw new Error("device id in use");
      devices.set(id, [...(devices.get(id) ?? []).filter((d) => d !== deviceId), deviceId]);
    },
    sessionLogout(id: string) {
      loggedOut.push(id);
    },
    walletUpdate(
      id: string,
      changeset: Record<string, number>,
      metadata: Record<string, unknown> = {},
      updateLedger = false,
    ) {
      const previous = { ...(wallets.get(id) ?? {}) };
      const current = { ...previous };
      for (const [k, v] of Object.entries(changeset)) {
        const next = (current[k] ?? 0) + v;
        if (next < 0) throw new Error("wallet: insufficient funds");
        current[k] = next;
      }
      wallets.set(id, current);
      if (updateLedger) {
        ledger.push({
          id: `l${String(ledger.length + 1)}`,
          userId: id,
          createTime: ledger.length + 1,
          updateTime: ledger.length + 1,
          changeset: { ...changeset },
          metadata: { ...metadata },
        });
      }
      return { userId: id, updated: current, previous };
    },
    walletLedgerList(id: string, limit = 100) {
      const items = ledger
        .filter((l) => l.userId === id)
        .slice()
        .reverse()
        .slice(0, limit);
      return { items };
    },
    friendsList(id: string, limit = 100, state?: number | null, cursor?: string) {
      const rows = [...edges(id).entries()]
        .filter(([, s]) => state === undefined || state === null || s === state)
        .map(([other, s]) => ({ user: toUser(user(other)), state: s, updateTime: 0 }));
      const start = cursor === undefined || cursor === "" ? 0 : Number(cursor);
      const page = rows.slice(start, start + limit);
      return {
        friends: page,
        cursor: start + limit < rows.length ? String(start + limit) : undefined,
      };
    },
    friendsAdd(id: string, _username: string, ids: string[]) {
      for (const other of ids) {
        if (edges(other).get(id) === 3) continue;
        if (edges(other).get(id) === 1) {
          edges(other).set(id, 0);
          edges(id).set(other, 0);
        } else if (edges(id).get(other) !== 0) {
          edges(id).set(other, 1);
          edges(other).set(id, 2);
        }
      }
      return { friends: [] };
    },
    friendsBlock(id: string, _username: string, ids: string[]) {
      for (const other of ids) {
        edges(id).set(other, 3);
        edges(other).delete(id);
      }
      return { friends: [] };
    },
    friendsDelete(id: string, _username: string, ids: string[]) {
      for (const other of ids) {
        edges(id).delete(other);
        edges(other).delete(id);
      }
      return { friends: [] };
    },
    matchCreate(module: string, params: Record<string, unknown>) {
      const matchId = `${String(matches.size + 1).padStart(8, "0")}-0000-4000-8000-000000000000.n1`;
      const rules = params["rules"] as { preset?: string } | undefined;
      matches.set(matchId, {
        size: 0,
        params,
        label: JSON.stringify({
          code: params["code"],
          gameId: params["gameId"] ?? module,
          mode: params["mode"],
          preset: rules?.preset ?? null,
          status: "open",
          seated: 0,
          open: 4,
          watching: 0,
          watchable: true,
        }),
      });
      return matchId;
    },
    matchGet(matchId: string) {
      const found = matches.get(matchId);
      return found === undefined
        ? null
        : { matchId, authoritative: true, size: found.size, label: found.label };
    },
    matchList: () => [],
    matchSignal(matchId: string, data: string) {
      signals.push({ matchId, data });
      return JSON.stringify({ ok: true, present: true });
    },
    streamCount: (stream: { subject?: string }) =>
      stream.subject !== undefined && online.has(stream.subject) ? 1 : 0,
    notificationSend(
      userId: string,
      subject: string,
      content: Record<string, unknown> = {},
      code = 0,
      senderId: string | null = null,
    ) {
      notifications.push({ userId, subject, code, content, senderId });
    },
    purchaseValidateApple: (userId: string, receipt: string, persist = true) =>
      validate("APPLE_APP_STORE", userId, receipt, persist),
    purchaseValidateGoogle: (userId: string, receipt: string, persist = true) =>
      validate("GOOGLE_PLAY_STORE", userId, receipt, persist),
    purchasesList(userId: string, limit = 100, cursor?: string) {
      const rows = purchases.filter((p) => p.userId === userId);
      const start = cursor === undefined || cursor === "" ? 0 : Number(cursor);
      const page = rows.slice(start, start + limit);
      return {
        validatedPurchases: page.map((p) => ({
          ...p,
          createTime: 1,
          updateTime: 1,
          providerResponse: "{}",
          seenBefore: true,
        })),
        cursor: start + limit < rows.length ? String(start + limit) : undefined,
      };
    },
    jwtGenerate: (algorithm: string, _key: string, claims: Record<string, unknown>) =>
      `${algorithm}.${Buffer.from(JSON.stringify(claims)).toString("base64url")}.sig`,
    httpRequest(url: string, method: string, headers: Record<string, string> = {}, body = "") {
      requests.push({ url, method, headers, body });
      return self.http(url, method, headers, body);
    },
    leaderboardCreate(id: string, ...rest: unknown[]) {
      // Nakama's order: authoritative, sort, operator, reset schedule, metadata, enable rank.
      const operator = typeof rest[2] === "string" ? rest[2] : "increment";
      const reset = rest.length > 3 ? rest[3] : "0 0 * * 1";
      if (!boards.has(id)) boards.set(id, new Map());
      boardMeta.set(id, { nextReset: reset === null ? 0 : 1_800_000_000, operator });
    },
    leaderboardsGetId: (ids: string[]) =>
      ids.flatMap((id) => {
        const meta = boardMeta.get(id);
        return meta === undefined
          ? []
          : [{ id, title: id, nextReset: meta.nextReset, prevReset: 0 }];
      }),
    leaderboardRecordWrite(
      id: string,
      ownerId: string,
      username: string | undefined,
      score = 0,
      subscore = 0,
      metadata: Record<string, unknown> = {},
    ) {
      const board = boards.get(id);
      if (board === undefined) throw new Error(`no leaderboard ${id}`);
      const current = board.get(ownerId) ?? { score: 0, numScore: 0, expiry: 0, metadata: {} };
      const next = {
        score: boardMeta.get(id)?.operator === "set" ? score : current.score + score,
        numScore: current.numScore + 1,
        expiry: current.expiry,
        metadata,
      };
      board.set(ownerId, next);
      return {
        leaderboardId: id,
        ownerId,
        username: username ?? user(ownerId).username,
        score: next.score,
        subscore,
        numScore: next.numScore,
        metadata,
        createTime: 0,
        updateTime: 0,
        expiryTime: next.expiry,
        rank: 0,
      };
    },
    leaderboardRecordsList(
      id: string,
      owners?: string[],
      limit = 100,
      _cursor?: string,
      overrideExpiry = 0,
    ) {
      const board =
        boards.get(id) ??
        new Map<
          string,
          { score: number; numScore: number; expiry: number; metadata: Record<string, unknown> }
        >();
      const ranked = [...board.entries()]
        .filter(([, r]) => r.expiry === overrideExpiry)
        .sort((a, b) => b[1].score - a[1].score)
        .map(([ownerId, r], i) => ({
          leaderboardId: id,
          ownerId,
          username: user(ownerId).username,
          score: r.score,
          subscore: 0,
          numScore: r.numScore,
          metadata: r.metadata,
          createTime: 0,
          updateTime: 0,
          expiryTime: r.expiry,
          rank: i + 1,
        }));
      const records = ranked.slice(0, limit);
      const ownerRecords =
        owners === undefined ? [] : ranked.filter((r) => owners.includes(r.ownerId));
      return { records, ownerRecords, rankCount: ranked.length };
    },
  } as unknown as nkruntime.Nakama;
  return {
    nk,
    storage,
    users,
    friends,
    online,
    matches,
    wallets,
    ledger,
    devices,
    deleted,
    loggedOut,
    signals,
    notifications,
    requests,
    purchases,
    get http() {
      return self.http;
    },
    set http(handler) {
      self.http = handler;
    },
    boards,
    user,
    object: (collection, key, userId) => storage.get(storageKey(collection, userId, key))?.value,
  };
}
