# Code style — write it so a human wrote it

This is the house style for session-migrate. The project goes to a public repo and takes outside
PRs, so the code must read as human-authored and be easy for a stranger to navigate. The rules below
are the *what*; `.editorconfig` + StyleCop.Analyzers + `dotnet format` are the *enforcement* (set up
at M1 — see `PORT_PLAN.md §6`). When a rule here and the analyzers disagree, fix the analyzer config,
not the code.

> Rubric adapted from the `humanize-code` skill (MIT, Lorcan Chinnock) — kept as local notes instead
> of installing the third-party plugin (zero supply-chain exposure; see the plugin audit in session).

## The one rule
**Match the surrounding code.** A new file should be indistinguishable from the file next to it —
same naming, same comment density, same idioms. Inconsistent-then-suddenly-perfect style is the
loudest "a machine wrote this" tell. Skim a few neighbouring files before you name anything.

## Naming
- **Drop filler nouns.** Cut `Manager`, `Handler`, `Helper`, `Util`, `Utility`, `Wrapper`, `Provider`,
  `Engine`, `Coordinator`, `Orchestrator`, `Controller` (outside MVC) when they add nothing.
  `UserDataManager` → `Users`. Keep the suffix only when it disambiguates from a real sibling type.
- **Drop type echoes.** `userList` → `users`. `customerMap` → `customersById`. `isEnabledFlag` →
  `isEnabled`. `cookieObj` → `cookie`.
- **Short, concrete names.** `retrieveCustomerInformationFromDatabase` → `LoadCustomer`.
  `calculateTotalAmount` → `Total`.
- **Cut Latinate verbs.** `utilise` → `use`, `instantiate` → `create`, `initialise` → `init`,
  `terminate` → `stop`/`end`, `aggregate` → `combine`/`sum`, `facilitate` → delete and name the real verb.
- **Booleans are questions.** `IsReady`, `HasItems`, `CanSign`, `ShouldRotate` — not `readyStatus`,
  `submittable`.
- **Keep well-known terms** as-is: `id`, `url`, `http`, `json`, `db`, `sql`, `regex`, `uuid`, `api`,
  `auth`, `oauth`, `jwt`, `tcp`, `dns`, `ip`, `csv`, `cli`, `gui`, `cache`, `queue`, `buffer`, `hash`,
  `key`, `value`, `payload`, `request`, `response`, `error`, `status`. Also project-domain terms that
  are precise here: `v10`, `v20`, `DBSC`, `PCPM`, `RTS`, `reseal`, `harvest`, `os_crypt`, `host_key`.
- **No unguessable abbreviations.** `usrCtxMgr`, `procReqHdlr` — spell out. Single letters fine for
  `i/j/k`, `x/y`, `e` (caught exception), `_` (discard).
- **Public API: consistency beats the rules.** Before renaming any public class/interface/method,
  check the sibling types. If `OrderManager`/`CustomerManager` already coexist, don't rename one in
  isolation — either rename the family or leave it and note it. Never half-rename: if you can't find
  every reference (reflection, generated code, external consumers), stop.

## Comments & docstrings
- **Cut comments that restate the code.** `// increment counter` over `counter++` → delete.
- **Keep comments that explain *why*.** Hidden constraint, surprising behaviour, workaround, invariant,
  link to a ticket. These are the valuable ones — rewrite them as plain sentences.
- **Short, active sentences.** Drop "It is important to note that", "This function is responsible for",
  "The purpose of this method is to". Just say what it does.
- **XML-doc only on the public `Core` API**, and about the *contract*, not the signature. No XML-doc
  spam on private helpers.
- **No em dashes in comments** (comma, full stop, or parentheses). No emoji. No AI vocabulary (list below).
- **Don't invent behaviour.** If a doc comment claims something the code doesn't do, fix the comment to
  match the code and flag the gap — don't leave the lie.

## Log / error / user-facing strings
- **State the fact, not the feeling.** `"FATAL: Catastrophic failure encountered while loading config!"`
  → `"failed to load config: {err}"`.
- **Concrete subject + object.** `"Operation failed"` → `"resealing cookie {host_key}/{name} failed: {reason}"`.
- **Include the values that help debugging** — identifiers, counts, paths, error codes (`NTE_*`, HRESULTs).
  **Redact secrets** (never log cookie values, keys, or harvested plaintext).
- **No empty politeness** (`please`, `kindly`, `oops`, `we apologise`), no trailing `!`. Match the
  nearest neighbouring log call's convention (casing, punctuation).

## What never changes
- Public API names, anything referenced from outside the repo, generated files, test-fixture/snapshot
  names, framework-required names (`Main`, `Dispose`, `render`), DB column names (`encrypted_value`,
  `host_key`, `creation_utc`), JSON field names that cross a wire. If unsure, ask.
- **Behaviour.** If a name is wrong because the code is wrong, fix the name and file the bug
  separately — never silently "fix" logic during a style pass.

## AI-vocabulary self-check (rewrite on any hit)
In comments / docstrings / strings: `leverage`, `seamless(ly)`, `robust`, `comprehensive`, `enhance`,
`facilitate`, `utilise`/`utilize`, `crucial`, `pivotal`, `vital`, `delve`, `holistic`, `streamline`.
Plus: em dashes (`—`) on changed lines; comments starting with "This function/method/class is
responsible for" or "The purpose of"; log strings starting with "ERROR:"/"FATAL:"/"Oops"/"Sorry,".

## Git / PR hygiene
- Commit: short imperative subject + a body that says *why* (and links the issue), not a vibes-only
  conventional-commit on every trivial change.
- README / PR text in your own voice: no `delve`/`leverage`/`comprehensive`, no rule-of-three padding,
  no emoji headers, no title-case in body text.
- New code matches the style of the files around it. That's the whole game.
