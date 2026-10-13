# ADR 0006: Active Master Data boundary at OpenTable

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D08.1, D08.3, D08.4, and D09

## Decision

Opening a new table session requires one valid Active Master Data package for the branch. The Branch reads that package and its billing policy inside the same serialized SQLite transaction that creates the table session, then persists a one-to-one `table_session_billing_policy` snapshot before audit, cloud outbox, and command receipt records are committed.

The session snapshot freezes `masterDataVersion`, `calculationPolicyVersion`, VAT rate, Service Charge rate, currency scale, and midpoint-rounding policy. A package activated later affects only newly opened table sessions. Calling `OpenTable` for a table that already has an active session returns that session and does not replace or recapture its policy.

If no Active package exists, `OpenTable` returns the idempotent rejection `ActiveMasterDataUnavailable` and creates no session. If an Active package is structurally incomplete, such as missing its billing policy, the transaction fails and rolls back rather than recording a permanent business rejection for corrupt infrastructure state.

Package provenance includes package schema version, SHA-256 checksum, publication time, and activation time. The application validates that the checksum is hexadecimal, activation does not precede publication, and the billing policy belongs to the same Master Data version.

## SQLite schema version 4

Schema version 4 adds:

- `master_data_packages`, with `Staged`, `Active`, and `Previous` lifecycle states
- a partial unique index allowing at most one Active package per branch
- a lifecycle index that permits retaining more than one historical Previous package
- `master_data_billing_policy`, linked to its package version

Version 1, 2, and 3 databases migrate forward atomically. Existing table-session billing snapshots are not rewritten or inferred from whichever package happens to be active during migration.

## Deliberately outside this slice

- Central package authoring, signing, publication, download, checksum verification, and atomic activation commands
- package rollback workflow and activation audit/outbox acknowledgements
- Item, SubItem, Option Group, Topping, price, branch-catalog, and production-station package tables
- Branch Sold Out operational overrides
- `SubmitOrder` and authoritative catalog resolution
- Card Charge policy per Payment Method
- public Branch API endpoints

There is intentionally no public package writer in this slice. Tests seed package rows directly; production package mutation must be introduced only with the verified download and atomic-activation workflow required by D08.1.
