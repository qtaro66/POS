# Contributing

## Workflow

1. Create a focused branch from current `main`.
2. Keep business-rule changes aligned with the versioned requirements.
3. Run the complete safe validation suite locally.
4. Push the branch and open a pull request.
5. Merge only after all required GitHub checks pass.

Direct pushes to `main` are not part of the normal workflow.

## Local validation

```powershell
dotnet restore POS.slnx
dotnet build POS.slnx --configuration Release --no-restore
dotnet test POS.slnx --configuration Release --no-build
npm ci
npm run lint
npm run build
git diff --check
```

Safe automated tests must not contact physical printers, cash drawers, production services, or production databases. Hardware testing requires a separate explicit opt-in workflow and operator confirmation.

## Public repository rules

Never commit customer data, employee data, credentials, private keys, private certificates, production databases, backups, real device identifiers, or local hardware evidence. Use documentation IP ranges and sanitized fixtures.
