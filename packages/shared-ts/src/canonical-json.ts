import { sha256Hex } from "./sha256";
import { utf8Encode } from "./utf8";

export type JsonPrimitive = string | number | boolean | null;

export interface JsonObject {
  readonly [key: string]: JsonValue | undefined;
}

export type JsonValue = JsonPrimitive | readonly JsonValue[] | JsonObject;

/**
 * Satisfied by any type whose reachable values are primitives, arrays or plain objects, so engine
 * states declared as interfaces or aliases hash without casts. Functions and symbols are rejected.
 */
export type JsonCompatible<T> = T extends JsonPrimitive | undefined
  ? T
  : T extends readonly (infer U)[]
    ? readonly JsonCompatible<U>[]
    : T extends (...args: never[]) => unknown
      ? never
      : T extends object
        ? { readonly [K in keyof T]: JsonCompatible<T[K]> }
        : never;

function isJsonArray(value: JsonValue): value is readonly JsonValue[] {
  return Array.isArray(value);
}

/**
 * Canonical JSON for state hashes (ARCHITECTURE 7.5): object keys sorted by UTF-16 code units, no
 * whitespace, integers only, strings escaped exactly as `JSON.stringify` does, arrays in stored
 * order, properties whose value is `undefined` omitted. Anything else is a programmer error.
 */
export function canonicalJson<T>(value: JsonCompatible<T> & T): string {
  return encode(value as JsonValue);
}

function encode(value: JsonValue): string {
  if (value === null) {
    return "null";
  }
  switch (typeof value) {
    case "string":
      return JSON.stringify(value);
    case "boolean":
      return value ? "true" : "false";
    case "number":
      if (!Number.isSafeInteger(value)) {
        throw new TypeError(`canonical JSON allows only safe integers, got ${String(value)}`);
      }
      return value === 0 ? "0" : String(value);
    case "object":
      break;
    default:
      throw new TypeError(`canonical JSON cannot encode a ${typeof value}`);
  }
  if (isJsonArray(value)) {
    const items = value.map((item: JsonValue | undefined) => {
      if (item === undefined) {
        throw new TypeError("canonical JSON arrays cannot hold undefined");
      }
      return encode(item);
    });
    return `[${items.join(",")}]`;
  }
  const keys = Object.keys(value).sort();
  const members: string[] = [];
  for (const key of keys) {
    const member = value[key];
    if (member !== undefined) {
      members.push(`${JSON.stringify(key)}:${encode(member)}`);
    }
  }
  return `{${members.join(",")}}`;
}

/** SHA-256 over the canonical JSON of a state, as a lowercase hex string (ADR-0006). */
export function stateHash<T>(value: JsonCompatible<T> & T): string {
  return sha256Hex(utf8Encode(canonicalJson(value)));
}
