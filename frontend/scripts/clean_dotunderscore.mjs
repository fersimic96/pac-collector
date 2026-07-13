#!/usr/bin/env node
/**
 * Cross-platform cleanup of macOS resource fork files (`._*`) that exFAT
 * volumes generate. No-op on Windows/Linux (NTFS/ext4 don't create them).
 *
 * Used as a pre-step for tests on Mac when the source tree lives on a
 * non-APFS volume (typical: external Transcend USB drive). On CI runners
 * the files don't exist and the script exits silently.
 */
import { readdirSync, statSync, unlinkSync } from "node:fs";
import { join } from "node:path";
import { platform } from "node:os";

if (platform() !== "darwin") {
  // Only macOS produces these files. Skip the walk entirely.
  process.exit(0);
}

const SKIP = new Set(["node_modules", "target", ".git", "dist", "release-builds"]);

function walk(dir) {
  let removed = 0;
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return 0;
  }
  for (const name of entries) {
    if (SKIP.has(name)) continue;
    const full = join(dir, name);
    let stat;
    try {
      stat = statSync(full);
    } catch {
      continue;
    }
    if (stat.isDirectory()) {
      removed += walk(full);
    } else if (name.startsWith("._")) {
      try {
        unlinkSync(full);
        removed++;
      } catch {}
    }
  }
  return removed;
}

const root = process.cwd();
const removed = walk(root);
if (removed > 0) {
  console.log(`clean:dotunderscore — removed ${removed} macOS metadata files`);
}
