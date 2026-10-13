/**
 * The drain switch for deploys (GDD 20.15, ARCHITECTURE 15): while draining, no new table opens
 * and no matchmaker ticket is accepted, but every running table plays to its end, so a node can
 * be retired without cutting a round. The flag lives in storage so every runtime VM sees it.
 */
export const OPS_COLLECTION = "ops";
export const OPS_KEY = "drain";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export interface DrainState {
  readonly draining: boolean;
  readonly since: number;
  readonly reason: string;
}

export const NOT_DRAINING: DrainState = { draining: false, since: 0, reason: "" };

export function readDrain(nk: nkruntime.Nakama): DrainState {
  try {
    const objects = nk.storageRead([
      { collection: OPS_COLLECTION, key: OPS_KEY, userId: SYSTEM_USER_ID },
    ]);
    const value = objects[0]?.value as Partial<DrainState> | undefined;
    if (value?.draining !== true) return NOT_DRAINING;
    return {
      draining: true,
      since: typeof value.since === "number" ? value.since : 0,
      reason: typeof value.reason === "string" ? value.reason : "",
    };
  } catch {
    return NOT_DRAINING;
  }
}

export function writeDrain(nk: nkruntime.Nakama, state: DrainState): void {
  nk.storageWrite([
    {
      collection: OPS_COLLECTION,
      key: OPS_KEY,
      userId: SYSTEM_USER_ID,
      value: state,
      permissionRead: 0,
      permissionWrite: 0,
    },
  ]);
}

export function isDraining(nk: nkruntime.Nakama): boolean {
  return readDrain(nk).draining;
}

/** Live tables of the game modules, for the deploy script to wait on. */
export function liveTables(nk: nkruntime.Nakama): number {
  try {
    return nk.matchList(1000, true, null, null, null, null).length;
  } catch {
    return -1;
  }
}
