#!/usr/bin/env node
/**
 * Lint: a .mmd diagram in resources/flows/ must be touched whenever the
 * corresponding flow logic in tools/flow-tracing.ts changes.
 *
 * §10.5: "Drift between diagram and code is exactly the kind of bug this
 * server exists to prevent."
 *
 * Behaviour:
 *   - For every .mmd under resources/flows/, check if the flow's entry
 *     point in tools/flow-tracing.ts has a NEWER mtime than the .mmd.
 *   - If so, exit 1 with a clear message.
 *
 * Touch the .mmd to silence the lint. There is no auto-update; diagrams
 * describe intent and the human owns the review.
 */

import { readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const FLOWS_DIR = resolve(__dirname, '../resources/flows');
const FLOWS_SCRIPT = resolve(__dirname, '../src/tools/flow-tracing.ts');

const scriptMtime = statSync(FLOWS_SCRIPT).mtimeMs;
const entries = readdirSync(FLOWS_DIR).filter((n) => n.endsWith('.mmd'));

let drift = 0;
for (const name of entries) {
  const mmdPath = resolve(FLOWS_DIR, name);
  const mmdMtime = statSync(mmdPath).mtimeMs;
  if (scriptMtime > mmdMtime) {
    drift++;
    const age = Math.round((scriptMtime - mmdMtime) / 1000);
    console.error(`[mmd-lint] ${name} is STALE (${age}s older than tools/flow-tracing.ts) — review and \`touch\` it.`);
  } else {
    console.log(`[mmd-lint] ${name} OK`);
  }
}

if (drift > 0) {
  console.error(`\n[mmd-lint] FAIL — ${drift} diagram(s) drifted. Run \`touch resources/flows/*.mmd\` after review, or update the diagrams.`);
  process.exit(1);
}
console.log('\n[mmd-lint] PASS — all diagrams in sync with flow-tracing.ts');
