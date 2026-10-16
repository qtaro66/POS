# Repository instructions

- Base decisions on facts, tests, and the versioned requirements in `docs/requirements`.
- Read requirements through the index of the reading copy, for example `docs/requirements/POS-Requirements-v0.2/README.md`: find the requirement ID or section number and open only those parts. Do not open the full source document; it is too large to read whole. After changing a requirements source, run `pwsh tools/split-requirements.ps1 -Source <source file>` and commit the regenerated reading copy with it.
- Do not change confirmed business rules silently. Record conflicts and obtain a decision before changing behavior.
- Keep domain and application code independent of Windows, Linux, database, browser, and printer implementations.
- The Branch API is the only process allowed to write the branch SQLite database.
- Browser and mobile clients must not connect directly to SQLite or physical printers.
- Safe automated tests must not contact hardware. Any physical printer or cash-drawer action requires explicit operator confirmation at execution time.
- After sending begins, an uncertain print result is `Unknown` and must not be retried automatically.
- Never commit secrets, private keys, real device identifiers, customer data, production databases, or local hardware evidence.

## Communication

- Reply to the user in concise English unless the user asks for another language. Requirements stay in their original language; quote Thai text only when the exact wording matters.
- Report outcomes, not process: what changed, the validation result, and any blocker or decision needed. Do not paste full command output, diffs, or file contents unless asked; reference the file, line, or log path instead.
- Keep handoff files, commit messages, pull request text, and code comments in English.

## OpenAI and Antigravity responsibilities

OpenAI agents are responsible for local development, analysis, and verification:

1. Analyze requirements, architecture, code, and risks.
2. Create a local task branch from the correct base commit.
3. Implement code and documentation within the requested scope.
4. Add or update automated tests.
5. Run the relevant tests, lint, build, and local security checks through `tools/verify.ps1` (see Verification gate).
6. Review the complete local diff for correctness, scope, secrets, and production data.
7. Create one or more local commits after validation succeeds.
8. Prepare a handoff summary for Antigravity.

OpenAI agents may delegate bounded analysis, implementation, test, or review work to subagents. The main OpenAI agent remains responsible for integrating delegated work into the local branch, resolving conflicts, running the combined validation, and preparing the final local commit.

OpenAI agents must stop after the validated local commit and handoff. Unless the user explicitly overrides this policy for a specific task, OpenAI agents must not:

- Push a branch or tag to any remote.
- Create, update, approve, close, or merge a pull request.
- Delete a remote branch or tag.
- Change repository settings, visibility, access, rules, secrets, or security configuration.
- Trigger a remote publication or deployment workflow.

Antigravity is the sole owner of the final GitHub workflow:

1. Inspect the handed-off local branch, commits, diff, and validation evidence.
2. Push the task branch.
3. Create or update the pull request.
4. Monitor required checks and review comments.
5. Apply or request corrections when needed.
6. Merge only after all required checks pass, no unresolved objection remains, and branch protection permits the merge.
7. Synchronize the local `main` branch and clean up the task branch after verifying the merge.

Every OpenAI-to-Antigravity handoff must include:

- Local branch name.
- Base commit and final local commit.
- Requested scope and implemented result.
- Files changed.
- Tests, lint, build, and security checks executed with their results.
- Known risks, limitations, and unresolved issues.
- Suggested pull request title and body.

Both agents must stop and obtain explicit user approval before force-pushing, rewriting published history, bypassing branch protection, merging with failed required checks, publishing secrets or production data, or making a material change outside the original task scope.

## Verification gate

`tools/verify.ps1` is the single local verification gate. `.github/workflows/verify.yml` runs the same script on `ubuntu-latest` and `windows-latest`, so a local pass predicts a CI pass. It covers repository hygiene, whitespace, secret scanning, the requirements reading copy, license headers (once REUSE is configured), .NET restore/format/build/test/vulnerable-package checks, and Node install/lint/test/build/audit.

- Agents run `pwsh tools/verify.ps1 -Quiet`: one line per step, plus a short excerpt for a failing step. Full output is saved under the branch's handoff directory in `verify-logs/`; open a log only when the excerpt is not enough. `-Quiet` changes only what is printed, not what is checked or reported.
- OpenAI must run `pwsh tools/verify.ps1 -Quiet` (or, for a person, double-click `tools/verify.cmd`) on the final local commit with a clean working tree before every handoff. It writes `validation.md` into the branch's handoff directory. A commit is validated only when that report shows `Commit` equal to the branch HEAD, `Working tree clean: yes`, and `Overall: PASS`.
- `verify.ps1` fails when the working tree has uncommitted or untracked files. While iterating, `-AllowDirty` runs the checks anyway, but such a report is never valid for handoff.
- Do not hand off with a failing step or with a step skipped on request (`-Skip` or `-AllowDirty`). A step reported `SKIPPED` because its component does not exist yet is acceptable.
- When CI fails on something `verify.ps1` did not catch, add that check to `verify.ps1` in the same correction round so it is caught locally next time.
- Tests that contact a printer, cash drawer, or other hardware must be tagged `[Trait("Category", "Hardware")]` (or the equivalent in other test frameworks). `verify.ps1` and CI never run them.
- Do not weaken `verify.ps1` or the CI workflows (removing steps, adding `-Skip`, lowering severity thresholds) without explicit user approval.

## Antigravity correction authority

Antigravity may make only these corrections itself, without a new OpenAI round:

- Formatting produced by `dotnet format` or the repository's configured formatter, with no other change.
- Lint auto-fixes that do not change behavior (import order, unused imports, whitespace).
- Typos in comments, documentation, and the pull request title or body.
- Purely mechanical merge-conflict resolutions (for example, both sides added different entries to a list), resolved by merging `main` into the task branch, never by rebasing pushed commits.

Each correction must be a separate commit prefixed `chore(antigravity):`, `verify.ps1` must pass on it before pushing, and the commit must be listed under "Corrections applied by Antigravity" in `from-antigravity.md`.

Everything else returns to OpenAI with `NEEDS_CHANGES`, including any change to business logic, tests, contracts or schemas, database migrations, payment, printing, security, dependencies, CI workflows, or `verify.ps1`. When unsure, return it to OpenAI.

## Changes requiring user approval before merge

Following requirements D21.3, a pull request that touches contracts or schemas, payment, printing, security or CI configuration, database migrations, or licensing needs explicit user approval before Antigravity merges it, even when all checks pass. `tools/check-handoff.ps1` lists these areas and adds a notice to the pull request body. Antigravity keeps such a pull request at `PR_OPEN`, records "user approval pending" under "Reviews and unresolved objections", and merges only after the user approves, either by an approving GitHub review from the code owner or by an explicit approval in the conversation.

## File-based handoff protocol

OpenAI and Antigravity exchange handoffs through ignored files in the same local repository. This protocol is available only when both agents use the same repository root and filesystem. If they are on different hosts, clones, or worktrees, stop and tell the user that file handoff is unavailable; do not assume that an ignored file will synchronize.

### Storage and identity

- Handoff files live under `.handoff/<branch-slug>--<branch-id>/`.
- Derive `branch-slug` from the exact branch name by replacing each run of characters outside `[A-Za-z0-9._-]` with `-`, trimming leading and trailing `-`, and converting to lowercase invariant.
- Derive `branch-id` as the first eight lowercase hexadecimal characters of SHA-256 over the UTF-8 bytes of the exact, case-sensitive branch name. The hash prevents collisions such as `feature/a-b` and `feature/a/b`.
- `.handoff/` must remain ignored by the repository root `.gitignore`. Never force-add, stage, commit, push, or attach files from `.handoff/`.
- Before every commit, `git ls-files -- .handoff` must return no paths. Before using this protocol, `git check-ignore .handoff/probe` must confirm that the directory is ignored.
- Handoff files follow the same data restrictions as commits: no secrets, private keys, real device identifiers, customer data, production databases, or local hardware evidence. Summarize sensitive evidence instead of copying it.
- Ignoring the directory prevents publication but does not encrypt it or protect it from other local processes.

### Files and writing rules

| File | Written by | Read by | Purpose |
| --- | --- | --- | --- |
| `to-antigravity.md` | OpenAI | Antigravity | Validated local commit ready for GitHub work |
| `from-antigravity.md` | Antigravity | OpenAI | PR state, merge result, or exact correction request |
| `log.md` | Both | Both | Append-only status history |
| `validation.md` | `tools/verify.ps1` | Both | Generated verification report for the validated commit |
| `pr-title.txt`, `pr-body.md` | `tools/check-handoff.ps1 -WritePr` | Antigravity | Pull request text for `gh pr create` |

- Use UTF-8 without a byte-order mark and LF line endings.
- Use UTC timestamps in ISO 8601 form, for example `2026-10-15T12:30:00Z`.
- Only one agent may write handoff files at a time. User trigger phrases create the sequencing boundary; agents must not run handoff mutations concurrently.
- Write `to-antigravity.md` and `from-antigravity.md` to a temporary file in the same directory, flush and close it, then atomically replace the destination. Never expose a partially written status file.
- Append exactly one complete line to `log.md` after the status file has been replaced. Use this format:

  ```text
  <timestamp> | agent=<OpenAI|Antigravity> | branch=<exact branch> | branchId=<branch-id> | round=<integer> | status=<status> | commit=<sha-or-none>
  ```

Every status file starts with this header:

```text
Protocol-Version: 1
Branch: <exact branch name>
Branch-Id: <branch-id>
Round: <integer starting at 1>
Status: <status defined below>
Updated: <UTC ISO 8601 timestamp>
```

Before acting, the reader must verify `Protocol-Version`, exact branch name, derived `Branch-Id`, round, allowed status, and the relevant commit. Stop and report any mismatch instead of guessing or repairing the file silently.

### Status and round ownership

- `to-antigravity.md` may use `READY` or `WITHDRAWN`.
- `from-antigravity.md` may use `PR_OPEN`, `NEEDS_CHANGES`, `BLOCKED`, or `MERGED`.
- OpenAI creates round 1 for the first `READY` handoff. After `NEEDS_CHANGES`, OpenAI increments the round only after producing and validating a new local commit.
- Antigravity always responds using the same round as the `READY` file it consumed.
- `PR_OPEN` remains in the same round. Resuming checks or reviewers must update that round rather than create a new one.
- `WITHDRAWN` invalidates that round. Antigravity must not push or resume it.

### Commit verification

- For `READY`, `Final local commit` must equal the local task branch HEAD, and the working tree must be clean except for ignored `.handoff/` files.
- For `PR_OPEN` and `NEEDS_CHANGES`, `Source commit checked` must equal the remote pull-request head inspected by Antigravity.
- For `MERGED`, record both the source commit checked and the resulting merge or squash commit on `main`.
- A squash merge normally changes the commit SHA. Verify the task content rather than requiring the source and merge SHA to match: compare the task's changed paths and resulting file states, or use an equivalent patch/content comparison that excludes unrelated concurrent changes on `main`. Record the method and result.
- Never declare content equivalence solely because the pull request reports `MERGED`.

### `to-antigravity.md` template

```text
Protocol-Version: 1
Branch:
Branch-Id:
Round:
Status: READY
Updated:

## Commits
- Base commit:
- Final local commit:

## Scope
- Requested:
- Implemented:

## Files changed
-

## Validation
<Copy the full content of validation.md produced by tools/verify.ps1 on the final local commit, unedited. Add rows below it only for extra checks run by hand.>

## Risks, limitations, unresolved issues
-

## Suggested pull request
Title:

Body:
```

### `from-antigravity.md` template

```text
Protocol-Version: 1
Branch:
Branch-Id:
Round:
Status:
Updated:

## Pull request
- URL:
- Source commit checked:
- Required checks: <passed, failed, or pending, with names>
- Reviews and unresolved objections:
- Mergeability:

## Merge result
- Main commit: <merge or squash commit; empty until merged>
- Content verification: <method and passed/failed; empty until merged>
- Local main synchronized: <yes/no>
- Task branch cleanup: <actual local and remote result after cleanup was attempted; never write pending in a final MERGED result>

## Corrections requested
<For NEEDS_CHANGES: one item per failed check or review comment, including file, line where available, evidence, and exact requested outcome. Leave empty otherwise.>

## Corrections applied by Antigravity
<Only small in-scope corrections, including commit hashes and changed files. OpenAI must fetch these before the next round. Leave empty otherwise.>

## Decision needed from user
<For BLOCKED only. Leave empty otherwise.>
```

### Trigger phrases

The user controls the sequence with these phrases:

- To OpenAI, **`handoff`**: use the current checked-out task branch. Reject `main`, a detached HEAD, a dirty worktree, or a commit without a passing `validation.md` (see Verification gate). Write the next `to-antigravity.md` round with `READY`, append `log.md`, and stop. This is the only trigger that may infer the branch from the current checkout.
- To Antigravity, **`take handoff <exact-branch>`** or **`รับ handoff <exact-branch>`**: the branch argument is mandatory. Run `pwsh tools/check-handoff.ps1 <exact-branch> -WritePr`, which resolves the handoff directory and validates the header, `READY`, the final commit against the local branch HEAD, and `validation.md`. If it exits non-zero, do not push; write `BLOCKED` with its output. Otherwise run the GitHub workflow, creating the pull request with the generated `pr-title.txt` and `pr-body.md`. Write `from-antigravity.md`, append `log.md`, and stop on a terminal status or `PR_OPEN`.
- To Antigravity, **`resume handoff <exact-branch>`**: the branch argument is mandatory. Resume a matching `PR_OPEN` in the same round, or a matching `MERGED` result only when it records a failed post-merge synchronization, content-verification, or cleanup operation. For `PR_OPEN`, recheck the pull-request head, required checks, reviews, security results, and mergeability. If GitHub already reports the pull request as merged, resume the post-merge synchronization, content verification, cleanup, and final result recording instead of attempting another merge. For `MERGED`, retry only the recorded failed post-merge operation and its dependent steps. Do not create a new round merely because checks were pending or post-merge finalization needed a retry.
- To OpenAI, **`take result <exact-branch>`** or **`รับผล <exact-branch>`**: the branch argument is mandatory even if Antigravity already switched to `main` or deleted the task branch. Read the matching `from-antigravity.md` and act by status:
  - `PR_OPEN`: report the pending checks or reviewers. Do not change code or create another round. Antigravity retains GitHub ownership.
  - `NEEDS_CHANGES`: fetch any commits explicitly listed under corrections applied by Antigravity, make only the requested in-scope corrections, revalidate, commit, and create the next `READY` round.
  - `BLOCKED`: summarize the exact user decision required and stop.
  - `MERGED`: verify the recorded main synchronization and content comparison, report completion, and do not create another round.

If a mandatory branch argument is missing, stop and request the exact branch name. If the shared repository prerequisite, directory identity, file header, round, status, or commit validation fails, stop and report the mismatch. Never search other handoff directories and select one by recency.

### GitHub ownership while `PR_OPEN`

Antigravity retains responsibility for the existing pull request while the status is `PR_OPEN`. OpenAI must not modify the task merely because checks or reviews are pending. The user may invoke `resume handoff <exact-branch>` until Antigravity records `MERGED`, `NEEDS_CHANGES`, or `BLOCKED`.

After GitHub reports the pull request as merged, Antigravity must complete the post-merge workflow in this order:

1. Switch to and synchronize local `main` with `origin/main`.
2. Verify that the merged result contains the task content using the content-comparison rule above.
3. Attempt both remote and local task-branch cleanup.
4. Write the final `MERGED` result with the actual outcome of synchronization, content verification, and each cleanup operation, then append its `MERGED` log entry.

Do not write `pending` for synchronization, verification, or cleanup in a final `MERGED` result. If a post-merge operation fails, the pull request is still merged and cannot be represented as `PR_OPEN`; write `MERGED`, record the exact failed operation and error in its corresponding merge-result field, and do not claim that operation succeeded. A later `resume handoff <exact-branch>` may retry only the failed post-merge operation and update the same round's `MERGED` result with another appended log entry.

If Antigravity is interrupted after GitHub merges the pull request but before the final `MERGED` file is written, the existing `PR_OPEN` file remains the recovery point. On the next `resume handoff`, detect the merged pull request and finish the post-merge workflow above. Ignored handoff files remain available after branch cleanup because they are not branch-specific Git content. OpenAI locates the result from the mandatory branch argument, not from the current checkout.

