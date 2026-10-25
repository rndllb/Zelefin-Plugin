import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

const HOOK_ID = /^[A-Za-z0-9_-]{43}$/;
const MAX_HOOKS = 50_000;
const PER_MINUTE = 30;

/**
 * Phones registered for webhook alerts (Emby, Silo), keyed by a random hook id that doubles as the
 * secret in the webhook address. Kept in one JSON file outside the app folder so a redeploy does
 * not drop them. A Silo hook also keeps the webhook's signing secret, to check each delivery.
 */
export class HookStore {
  constructor(file) {
    this.file = file;
    this.hooks = new Map();
    this.recent = new Map();
    this.saveTimer = null;
    try {
      const saved = JSON.parse(fs.readFileSync(file, "utf8"));
      for (const [id, hook] of Object.entries(saved.hooks || {})) {
        if (HOOK_ID.test(id) && hook?.token && hook?.platform) {
          this.hooks.set(id, hook);
        }
      }
    } catch {
      // No file yet, or unreadable: start empty.
    }
  }

  create({ platform, token, label }) {
    if (this.hooks.size >= MAX_HOOKS) {
      return null;
    }
    const id = crypto.randomBytes(32).toString("base64url");
    const now = new Date().toISOString();
    this.hooks.set(id, { platform, token, label: label || "", created: now, updated: now });
    return this.save() ? id : (this.hooks.delete(id), null);
  }

  get(id) {
    return HOOK_ID.test(String(id)) ? this.hooks.get(id) || null : null;
  }

  update(id, { platform, token, label, signingSecret }) {
    const hook = this.get(id);
    if (!hook) {
      return false;
    }
    const next = { ...hook, platform, token, updated: new Date().toISOString() };
    if (label !== undefined) {
      next.label = label;
    }
    if (signingSecret !== undefined) {
      next.signingSecret = signingSecret;
    }
    this.hooks.set(id, next);
    this.save();
    return true;
  }

  remove(id) {
    if (this.hooks.delete(id)) {
      this.save();
    }
  }

  /** At most [PER_MINUTE] alerts a minute per phone, so a big library scan cannot flood it. */
  allow(id) {
    const now = Date.now();
    const times = (this.recent.get(id) || []).filter((time) => now - time < 60_000);
    if (times.length >= PER_MINUTE) {
      this.recent.set(id, times);
      return false;
    }
    times.push(now);
    this.recent.set(id, times);
    return true;
  }

  save() {
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true, mode: 0o700 });
      const temporary = `${this.file}.${process.pid}.tmp`;
      fs.writeFileSync(temporary, JSON.stringify({ hooks: Object.fromEntries(this.hooks) }), { mode: 0o600 });
      fs.renameSync(temporary, this.file);
      return true;
    } catch (error) {
      console.warn(`emby hooks not saved: ${error.message}`);
      return false;
    }
  }
}
