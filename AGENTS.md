# Repository instructions

- Base decisions on facts, tests, and the versioned requirements in `docs/requirements`.
- Do not change confirmed business rules silently. Record conflicts and obtain a decision before changing behavior.
- Keep domain and application code independent of Windows, Linux, database, browser, and printer implementations.
- The Branch API is the only process allowed to write the branch SQLite database.
- Browser and mobile clients must not connect directly to SQLite or physical printers.
- Safe automated tests must not contact hardware. Any physical printer or cash-drawer action requires explicit operator confirmation at execution time.
- After sending begins, an uncertain print result is `Unknown` and must not be retried automatically.
- Never commit secrets, private keys, real device identifiers, customer data, production databases, or local hardware evidence.

## GitHub workflow authorization

When a user asks to change or develop this project and the work must be published to GitHub, the agent is authorized to complete the standard GitHub workflow without requesting confirmation at each step:

1. Create a feature branch.
2. Change code and documentation within the requested scope.
3. Run the relevant tests, lint, build, and security checks.
4. Commit and push the feature branch.
5. Create or update a pull request.
6. Inspect review comments, mergeability, and required checks.
7. Fix in-scope failures and push the corrections.
8. Merge the pull request after all required checks pass, no unresolved objection remains, and branch protection permits the merge.
9. Update the local `main` branch to match `origin/main`.
10. Delete the task's feature branch after verifying that its content is present on `main`.
11. Report the final result with the pull request URL, resulting commit, and verification results.

Do not pause to request approval during the standard workflow above.

Stop and obtain explicit user approval before any of the following:

- Force-pushing or rewriting Git history.
- Bypassing branch protection or required checks.
- Merging while tests, security checks, or required checks are failing.
- Deleting a repository, release, tag, or branch not created for the current task.
- Changing repository visibility, user access, or security settings.
- Publishing a secret, credential, private key, or real production data.
- Making a material change outside the original task scope.

