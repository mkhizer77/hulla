# @hulla/codegen

`pnpm codegen` (from the root) regenerates `packages/protocol/generated/`:
- `ts/protocol.ts`: one interface per message payload, the envelope and the Thulla view, from a root schema assembled out of the opcode registry;
- `ts/validators.ts`: a zod validator per message payload plus the envelope, from the dereferenced schemas; `payloadValidators[type]` is what the server and the web client run on every inbound message;
- `ts/opcodes.ts`: the registry as a typed constant;
- `csharp/Protocol.cs`: the C# DTOs (Newtonsoft.Json attributes) for Unity, plus the UPM `package.json` and `Hulla.Protocol.asmdef` that let the Unity project reference the folder as a local package.

Every `$ref` is resolved to its absolute `$id` before generation, so one resolver serves all three generators. The output is deterministic; CI regenerates and fails on any diff. Never edit the generated folders by hand.
