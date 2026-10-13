import { Client, type Session, type Socket } from "@heroiclabs/nakama-js";

import type { MessageEnvelope } from "@hulla/protocol/generated";

export const NAKAMA_HOST = process.env["HULLA_NAKAMA_HOST"];
export const NAKAMA_PORT = process.env["HULLA_NAKAMA_PORT"] ?? "7350";
export const SERVER_KEY = process.env["HULLA_NAKAMA_SERVER_KEY"] ?? "local-only-server-key";

/** A scripted client: records every envelope it receives and can wait for the next matching one. */
export interface ScriptedClient {
  readonly name: string;
  readonly session: Session;
  readonly socket: Socket;
  readonly received: MessageEnvelope[];
  send(matchId: string, type: string, opcode: number, payload: object): Promise<void>;
  next(
    predicate: (envelope: MessageEnvelope) => boolean,
    timeoutMs?: number,
  ): Promise<MessageEnvelope>;
  close(): void;
}

function decode(data: Uint8Array | string): MessageEnvelope {
  const text = typeof data === "string" ? data : new TextDecoder().decode(data);
  return JSON.parse(text) as MessageEnvelope;
}

export async function scriptedClient(name: string, suffix: string): Promise<ScriptedClient> {
  if (NAKAMA_HOST === undefined) throw new Error("HULLA_NAKAMA_HOST is not set");
  const client = new Client(SERVER_KEY, NAKAMA_HOST, NAKAMA_PORT, false, 10_000, true);
  const session = await client.authenticateDevice(
    `hulla-it-${name}-${suffix}`,
    true,
    `${name}${suffix.slice(-9)}`,
  );
  const socket = client.createSocket(false, false);
  const received: MessageEnvelope[] = [];
  const waiters: {
    predicate: (e: MessageEnvelope) => boolean;
    resolve: (e: MessageEnvelope) => void;
  }[] = [];
  socket.onmatchdata = (m) => {
    const envelope = decode(m.data);
    received.push(envelope);
    for (let i = waiters.length - 1; i >= 0; i -= 1) {
      const waiter = waiters[i];
      if (waiter?.predicate(envelope) === true) {
        waiters.splice(i, 1);
        waiter.resolve(envelope);
      }
    }
  };
  await socket.connect(session, false);
  let clientSeq = 0;
  return {
    name,
    session,
    socket,
    received,
    async send(matchId, type, opcode, payload) {
      clientSeq += 1;
      const envelope: MessageEnvelope = {
        v: 1,
        type,
        tableId: matchTableId,
        seq: clientSeq,
        ts: Date.now(),
        payload,
      };
      await socket.sendMatchState(matchId, opcode, JSON.stringify(envelope));
    },
    next(predicate, timeoutMs = 20_000) {
      const already = received.find(predicate);
      if (already !== undefined) return Promise.resolve(already);
      return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
          const index = waiters.findIndex((w) => w.resolve === done);
          if (index !== -1) waiters.splice(index, 1);
          reject(
            new Error(
              `${name}: timed out waiting; last types ${received
                .slice(-5)
                .map((e) => e.type)
                .join(",")}`,
            ),
          );
        }, timeoutMs);
        const done = (e: MessageEnvelope): void => {
          clearTimeout(timer);
          resolve(e);
        };
        waiters.push({ predicate, resolve: done });
      });
    },
    close() {
      socket.disconnect(false);
    },
  };
}

/** The table id every envelope carries; set once the table is created. */
export let matchTableId = "";

export function setTableId(tableId: string): void {
  matchTableId = tableId;
}

export function rpc<T>(client: ScriptedClient, id: string, input: object): Promise<T> {
  if (NAKAMA_HOST === undefined) throw new Error("HULLA_NAKAMA_HOST is not set");
  const api = new Client(SERVER_KEY, NAKAMA_HOST, NAKAMA_PORT, false, 10_000, true);
  return api.rpc(client.session, id, input).then((r) => r.payload as T);
}
