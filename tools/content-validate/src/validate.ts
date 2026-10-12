import { join } from "node:path";

import { validateContentTree } from "./content";

const root = join(import.meta.dirname, "..", "..", "..");
const problems = validateContentTree(join(root, "content"));
for (const problem of problems) console.error(problem);
if (problems.length > 0) {
  console.error(`content: ${String(problems.length)} problem(s)`);
  process.exit(1);
}
console.log("content: every file is valid");
