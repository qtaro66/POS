# POS

Open-source, offline-capable restaurant POS platform for branch PC, Android/iOS mobile clients, central back office, and direct network thermal printing.

## Status

This repository is an initial architecture scaffold. It is not production-ready and must not be used to process real payments or production print jobs.

The temporary checkout is `C:\POS`. When the second internal NVMe is ready, create a fresh clone on the new NTFS volume and validate it before removing this checkout. Do not move the `.git` directory manually.

## Architecture

The opt-in Branch ordering API is documented in
[`docs/api/branch-ordering-v1.md`](docs/api/branch-ordering-v1.md).

- `apps/pos-web` — cashier PC web application for Chromium/Edge kiosk
- `apps/mobile-pwa` — Phase 0 Android/iOS installable PWA
- `apps/backoffice` — central administration web application
- `src/Branch/Pos.Branch.Api` — branch API and the only owner of the branch SQLite database
- `src/Central/Pos.Central.Api` — central API backed by PostgreSQL
- `src/BuildingBlocks` — contracts, domain, and application layers
- `src/Printing` — printer abstractions and protocol adapters
- `src/Platform` — Windows/Linux hosting integrations only
- `tests` — automated tests that are safe by default
- `docs/requirements` — versioned product requirements; read them through the generated reading copy index (for example `docs/requirements/POS-Requirements-v0.2/README.md`), regenerated with `tools/split-requirements.ps1`
- `deploy` — Windows Service and Linux systemd packaging assets

## Toolchain

- .NET SDK 10.0.401
- Node.js 24 or newer
- npm workspaces
- React, TypeScript, and Vite

## Build

```powershell
dotnet restore POS.slnx
dotnet build POS.slnx --no-restore
dotnet test POS.slnx --no-build

npm ci
npm run lint
npm run build
```

GitHub pull requests run the same .NET and frontend checks automatically. Dependency Review rejects newly introduced dependencies with known high or critical severity findings.

## Agent continuation

After a READY handoff is tracked, send OpenAI `continue` to consume that exact
result and prepare the next validated local handoff when the merge is complete.
Pending PRs remain with Antigravity; approvals and GitHub ownership are unchanged.

```powershell
# Track an explicitly selected, validated READY handoff (normally done by OpenAI).
pwsh tools/continue-handoff.ps1 -TrackBranch 'feature/exact-task-branch' -Round 1
# Read the next action without changing Git, code, or remote state.
pwsh tools/continue-handoff.ps1
```

Tracking is local to this checkout in ignored `.handoff/continue.json` and is
not shared across clones or worktrees. See `AGENTS.md` for sequencing rules.

## Safety boundaries

- Automated tests must not contact a physical printer, open a cash drawer, or use production endpoints.
- Hardware tests require a separate explicit opt-in entrypoint and a non-production printer profile.
- A successful socket write is not proof that paper was printed.
- Secrets, private certificates, runtime databases, backups, logs, and local hardware evidence are excluded from Git.

## License

Source code is licensed under the Mozilla Public License 2.0. Third-party assets retain their own licenses and notices.
See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for bundled components that are licensed separately.

