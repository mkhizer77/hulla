/** Opcode registry, schema loading helpers, and the generated types, validators and opcode table. */
export {
  PROTOCOL_MAJOR,
  loadOpcodes,
  loadSchemas,
  type OpcodeEntry,
  type Visibility,
} from "./registry";
export * from "../generated/ts";
