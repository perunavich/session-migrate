# Roadmap

session-migrate started as a clean Chromium-profile migrator. The goal is broader: **migrate a
user's logged-in sessions across machines for the software they actually live in** — not just
browsers, and not just Chromium.

This is a living document — open an issue or PR to discuss anything here.

## Now (shipping)

- **Chromium browsers** — Chrome, Edge, Brave, Vivaldi, Opera/Opera GX, Yandex, Arc, Chromium,
  Thorium. Bearer cookies + passwords + autofill + Local/Session Storage + IndexedDB re-keyed and
  carried. App-Bound (v20) via live harvest. DBSC device-bound sessions flagged honestly.

## Planned

### Non-Chromium browsers
- **Firefox / Gecko** — different profile layout (`logins.json` + `key4.db` NSS, `cookies.sqlite`,
  `sessionstore`); needs an NSS-based key path rather than DPAPI/os_crypt.
- **Safari / WebKit** (where reachable) — investigate.

### Top ~30 popular desktop apps
Many ship Chromium under the hood (Electron), so the cookie/Local Storage approach often transfers;
each still needs per-app profile-path mapping and validation. Native apps need bespoke handling.
Target set to triage (chat, dev, media, productivity), e.g.:
- Chat: Slack, Discord, Microsoft Teams, Telegram Desktop, WhatsApp, Signal.
- Dev: VS Code (+ settings sync state), GitHub Desktop, Postman, Docker Desktop.
- Media: Spotify, Steam.
- Productivity: Notion, Obsidian, Figma, Linear, ClickUp, Todoist.
- (curate the final list; not all are migratable without a re-login.)

### Cross-cutting
- **Per-app capability report** — for each target, say honestly what migrates vs. needs a re-login
  (the same honesty the browser report already gives for DBSC / App-Bound).
- **Locked-store handling** — auto-fall-back to a VSS snapshot when a store is exclusively locked and
  the process is elevated, instead of asking the user to close the app.
- **Linux/macOS** — the offline crypto is Windows-specific (DPAPI); generalize the key layer.

## Non-goals

- No malware techniques (process injection, impersonation, key-extraction exploits, anti-detection).
- No defeating server-side anti-fraud (IP/fingerprint) — that's probabilistic and out of scope.
- Device-bound (TPM) sessions are reported as "1 re-login on another machine", never faked.
