# Contributing

Thanks for taking a look. This is a small, focused tool; the bar is correctness and readable code.

## Build, test, format

```sh
dotnet test SessionMigrate.sln
dotnet format SessionMigrate.sln                       # apply formatting
dotnet format SessionMigrate.sln --verify-no-changes   # the CI gate
```

Target framework is `net10.0-windows`, x64. The app is Windows-only (DPAPI / TPM / Chromium paths).

## House style

Read [STYLE.md](STYLE.md). The short version: match the surrounding code, comment *why* not *what*,
name things from the domain, and keep error/log strings factual. `.editorconfig` + StyleCop enforce
the mechanical parts; `dotnet format --verify-no-changes` must pass.

## Definition of done (per change)

1. **Structured at authoring time** — small functions, one responsibility per type.
2. **Green test** — add or extend a test that pins the behavior. Core changes are offline and
   deterministic; don't reach for the network or a live browser in a unit test.
3. **A cleanup pass** — no dead code, no copy-paste, no needless complexity.
4. **Green analyzers** — `dotnet format --verify-no-changes` clean, build with no new warnings.

Keep PRs milestone-sized so they're reviewable. Commit messages: a short imperative subject and a
body that says *why*.

## The one hard rule: no malware techniques

This tool ships clean. It does not — and will not — use process injection, elevation/impersonation
tricks, anti-debug/anti-detection, or exploits to extract keys. It moves plaintext values the running
browser already exposes to its own user and re-encrypts them on the destination. If a change can't be
done within that principle, it doesn't belong here.

DBSC / device-bound sessions are reported honestly ("1 re-login on a new machine"), never faked.
