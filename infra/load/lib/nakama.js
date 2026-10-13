// Nakama over REST and WebSocket for k6 (GDD 28.1). JSON envelopes on the socket (`format=json`),
// match data as base64 strings, request ids (`cid`) for the calls that answer.
import encoding from "k6/encoding";
import http from "k6/http";
import { check } from "k6";

export const HOST = __ENV.HULLA_HOST || "127.0.0.1";
export const PORT = __ENV.HULLA_PORT || "7350";
export const SERVER_KEY = __ENV.HULLA_SERVER_KEY || "local-only-server-key";
export const TLS = (__ENV.HULLA_TLS || "false") === "true";

const base = `${TLS ? "https" : "http"}://${HOST}:${PORT}`;
const basic = `Basic ${encoding.b64encode(`${SERVER_KEY}:`)}`;

/** A device sign-in; every VU and seat gets its own device id. */
export function authenticate(deviceId, username) {
  const res = http.post(
    `${base}/v2/account/authenticate/device?create=true&username=${encodeURIComponent(username)}`,
    JSON.stringify({ id: deviceId }),
    {
      headers: { Authorization: basic, "Content-Type": "application/json" },
      tags: { name: "authenticate" },
    },
  );
  check(res, { authenticated: (r) => r.status === 200 });
  if (res.status !== 200) return null;
  return JSON.parse(res.body);
}

export function rpc(session, id, payload) {
  const res = http.post(`${base}/v2/rpc/${id}?unwrap`, JSON.stringify(payload || {}), {
    headers: { Authorization: `Bearer ${session.token}`, "Content-Type": "application/json" },
    tags: { name: `rpc ${id}` },
  });
  check(res, { [`${id} 200`]: (r) => r.status === 200 });
  if (res.status !== 200) return null;
  return JSON.parse(res.body);
}

export function socketUrl(session) {
  return `${TLS ? "wss" : "ws"}://${HOST}:${PORT}/ws?lang=en&status=false&format=json&token=${session.token}`;
}

export function encodeData(envelope) {
  return encoding.b64encode(JSON.stringify(envelope));
}

export function decodeData(data) {
  return JSON.parse(encoding.b64decode(data, "std", "s"));
}

/** `{ cid, match_join }` and `{ cid, match_data_send }` as Nakama's JSON socket protocol spells them. */
export function joinMessage(cid, matchId, metadata) {
  return JSON.stringify({
    cid: String(cid),
    match_join: { match_id: matchId, metadata: metadata || {} },
  });
}

export function dataMessage(matchId, opCode, envelope) {
  return JSON.stringify({
    match_data_send: { match_id: matchId, op_code: String(opCode), data: encodeData(envelope) },
  });
}
