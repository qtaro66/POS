# Repository instructions

- Base decisions on facts, tests, and the versioned requirements in `docs/requirements`.
- Do not change confirmed business rules silently. Record conflicts and obtain a decision before changing behavior.
- Keep domain and application code independent of Windows, Linux, database, browser, and printer implementations.
- The Branch API is the only process allowed to write the branch SQLite database.
- Browser and mobile clients must not connect directly to SQLite or physical printers.
- Safe automated tests must not contact hardware. Any physical printer or cash-drawer action requires explicit operator confirmation at execution time.
- After sending begins, an uncertain print result is `Unknown` and must not be retried automatically.
- Never commit secrets, private keys, real device identifiers, customer data, production databases, or local hardware evidence.

## OpenAI and Antigravity responsibilities

OpenAI agents are responsible for local development, analysis, and verification:

1. Analyze requirements, architecture, code, and risks.
2. Create a local task branch from the correct base commit.
3. Implement code and documentation within the requested scope.
4. Add or update automated tests.
5. Run the relevant tests, lint, build, and local security checks.
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

