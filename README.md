# session-migrate

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)](#requirements)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![Tests: 31 passing](https://img.shields.io/badge/tests-31%20passing-success)](#build--run)
[![Download](https://img.shields.io/badge/download-latest%20release-brightgreen?logo=github)](https://github.com/perunavich/session-migrate/releases/latest)

Migrate a Chromium browser profile between Windows machines or users — or across a Windows
reinstall — so your web logins survive. **No malware techniques:** no process injection, no elevation
or impersonation tricks, no key-extraction exploits. It reads each value as plaintext on the source,
moves it, and re-encrypts it under a fresh key on the destination.

Target: any Chromium browser (Chrome, Edge, Brave, Vivaldi, Opera…). Windows-only.

## Download

Grab the prebuilt, self-contained Windows binaries from the
**[latest release](https://github.com/perunavich/session-migrate/releases/latest)** — no .NET install
needed to run them:

| File | What it is |
|---|---|
| **`SessionMigrate.Ui.exe`** | The desktop app. Double-click to run. |
| **`session-migrate-cli.zip`** | The CLI (`session-migrate.exe`) plus `assets/cookie-export-ext/`, the harvest extension. |

Prefer to build from source instead? See [Build & run](#build--run).

## What survives a move

- **Bearer-token sessions** — most of the web (GitHub, Reddit, GitLab, …). Migrate reliably.
- **Passwords, autofill, OAuth tokens** — re-keyed (Login Data, Web Data).
- **Local / Session Storage, IndexedDB, Service Workers, extensions** — carried verbatim.
- **Google / Microsoft DBSC (device-bound) sessions** — the signing key lives in the *source*
  machine's TPM and cannot leave it. Cross-machine these need **one re-login**, and the tool says so
  honestly rather than pretending otherwise.

## How it works

The os_crypt key that protects a Chromium profile is wrapped with Windows DPAPI. session-migrate
reads the source key, mints a **fresh** key in the destination's `Local State`, copies the profile
tree, then re-encrypts every encrypted SQLite store (cookies, passwords, tokens) under the new key.
Modern Chrome binds each cookie's value to its host with a `SHA-256(host_key)` prefix; that prefix is
preserved byte-for-byte. App-Bound (v20) values can't be read from a file offline and are left as-is.

The whole offline pipeline is deterministic and unit-tested — no network, no live browser required.

## Requirements

- **.NET 10 SDK** (Windows)
- **WebView2 Runtime** (ships with Windows 11; needed only for the UI)

## Build & run

```sh
dotnet test SessionMigrate.sln          # build + run all tests
dotnet run --project Ui                    # the desktop UI
dotnet format SessionMigrate.sln --verify-no-changes   # CI gate
```

In the UI: pick a source browser and profile → **Analyze** to see what's there → **Migrate full
profile** to an empty folder. Point a Chromium browser at the destination's `User Data` folder to use
the migrated profile.

> **Close the source browser before migrating.** A running browser exclusively locks its `Cookies`
> file, so those rows are skipped in a live copy.

`NuGet.config` adds nuget.org for this solution (in case the machine's global config only has the
Visual Studio offline cache).

### Standalone binary

`./build-release.ps1` produces self-contained, single-file Windows exes under `publish/` (gitignored,
meant to ship as release assets, not committed) — no .NET install required to run them:

- `publish/ui/SessionMigrate.Ui.exe` — the desktop app.
- `publish/cli/session-migrate.exe` — the CLI (with `assets/cookie-export-ext/`, the harvest
  extension you load into Chrome via `chrome://extensions` → Developer mode → Load unpacked).

## Project layout

| Project | What it is |
|---|---|
| `Core/` | Crypto, storage, migration, harvest, CDP, TPM. No UI; unit-tested. This is where the risk lives. |
| `Cli/`  | Console front end exposing the whole toolkit (`assets/cookie-export-ext/` is the harvest extension). |
| `Ui/`   | WinForms + WebView2 shell; the HTML/CSS/JS UI is under `Ui/web/`. |
| `Core.Tests/` | xUnit. Golden-vector, round-trip, and full-migration tests (offline, deterministic). |

## Command line

`dotnet run --project Cli -- <command>` (or run `session-migrate.exe`):

| Command | What it does |
|---|---|
| `detect` | List installed Chromium browsers and their profiles. |
| `report <userDataDir> <profile>` | Scheme, cookie counts, DBSC/App-Bound flags, top sites. |
| `migrate <srcUserData> <profile> <destUserData>` | Full-profile clone, re-keyed to a fresh os_crypt key. |
| `export <srcUserData> <profile> <browser> <bundleDir> <passphrase>` | Passphrase-protected bundle. |
| `import <bundleDir> <destUserData> <passphrase>` | Restore a bundle on another machine. |
| `harvest [port] [out.json]` | Receive plaintext cookies from the export extension (App-Bound v20). |
| `cdp-harvest <port> <out.json>` | Harvest from a running browser over DevTools. |
| `cdp-verify <port> <targets.json>` | Probe login state per target page. |
| `dbsc-build <srcUserData> <profile> <destUserData> <harvest.json> [--force-rotate]` | Same-machine DBSC restore. |
| `tpm-check <DeviceBoundSessionsDb>` | Re-import the DBSC TPM key and sign+verify (proves it's usable here). |
| `snapshot <base> <profile> <destDir> <item>…` | VSS shadow copy of locked stores (admin). |

## Scope & honest limits

- **Same-machine reinstall:** bearer sessions and most profile data migrate; DBSC works only if the
  TPM is intact — do **not** Clear the TPM during reinstall.
- **Cross-machine:** bearer migrates; DBSC = one re-login (different TPM, by design).
- **App-Bound (v20) cookies** require reading from a live browser; that path is out of scope for the
  offline tool here.
- Server-side anti-fraud (IP / fingerprint) is probabilistic and not something any migrator defeats.

## Roadmap

The aim is broader than Chromium browsers — non-Chromium browsers and the popular desktop apps people
live in. See [ROADMAP.md](ROADMAP.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). `dotnet format` is the gate; keep new code in the style of
the files around it.

## License

[MIT](LICENSE).
