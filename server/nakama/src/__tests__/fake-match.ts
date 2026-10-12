/** Records what a match handler asked Nakama to do: broadcasts, kicks, labels and storage. */
export interface Broadcast {
  readonly opcode: number;
  readonly envelope: { type: string; seq: number; payload: unknown };
  readonly to: string[] | null;
}

export interface FakeDispatcher {
  readonly dispatcher: nkruntime.MatchDispatcher;
  readonly broadcasts: Broadcast[];
  readonly kicks: string[][];
  readonly labels: string[];
}

export function fakeDispatcher(): FakeDispatcher {
  const broadcasts: Broadcast[] = [];
  const kicks: string[][] = [];
  const labels: string[] = [];
  const dispatcher = {
    broadcastMessage(opcode: number, data: string, presences: nkruntime.Presence[] | null) {
      broadcasts.push({
        opcode,
        envelope: JSON.parse(data) as Broadcast["envelope"],
        to: presences === null ? null : presences.map((p) => p.sessionId),
      });
    },
    broadcastMessageDeferred() {
      throw new Error("not used");
    },
    matchKick(presences: nkruntime.Presence[]) {
      kicks.push(presences.map((p) => p.sessionId));
    },
    matchLabelUpdate(label: string) {
      labels.push(label);
    },
  } as unknown as nkruntime.MatchDispatcher;
  return { dispatcher, broadcasts, kicks, labels };
}

export interface FakeMatchNk {
  readonly nk: nkruntime.Nakama;
  readonly writes: nkruntime.StorageWriteRequest[];
}

export function fakeMatchNk(): FakeMatchNk {
  const writes: nkruntime.StorageWriteRequest[] = [];
  let uuids = 0;
  const nk = {
    uuidv4: () => {
      uuids += 1;
      return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
    },
    sha256Hash: (input: string) => `sha256(${input})`,
    binaryToString: (data: ArrayBuffer) => new TextDecoder().decode(data),
    storageWrite: (requests: nkruntime.StorageWriteRequest[]) => {
      writes.push(...requests);
      return [];
    },
  } as unknown as nkruntime.Nakama;
  return { nk, writes };
}

export function nkPresence(i: number): nkruntime.Presence {
  return {
    sessionId: `s${String(i)}`,
    userId: `u${String(i)}`,
    username: `Player${String(i)}`,
    node: "n1",
  };
}

export function matchMessage(
  sender: nkruntime.Presence,
  opCode: number,
  envelope: object,
  asBinary = false,
): nkruntime.MatchMessage {
  const text = JSON.stringify(envelope);
  const data = asBinary ? new TextEncoder().encode(text).buffer : (text as unknown as ArrayBuffer);
  return { sender, persistence: false, status: "", opCode, data, reliable: true, receiveTimeMs: 0 };
}
