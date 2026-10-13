# ADR 0007: Frozen Catalog snapshot with current sellability validation

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D08.1-D08.4 and D09

## Decision

Catalog resolution separates immutable session content from current operational availability.

- The table session's `master_data_version` selects the Item/SubItem/Topping display names, resolved prices, Option Group structure, and limits used to create an Order Line snapshot.
- The branch's currently Active package supplies `CentralActive`, `BranchCatalogEnabled`, and current price availability for every selected Item and Option.
- `branch_sold_out_overrides` supplies the current branch Sold Out state independently from every Master Data package.

Consequently, activating a newer package does not reprice or rename a Draft in an existing table session, while disabling, removing, making price-less, or marking any selected Item/Option Sold Out blocks the next `SubmitOrder` validation immediately.

The resolver must run on the same SQLite connection and transaction as the future `SubmitOrder` commit. It never trusts a client-provided price, name, Option membership, or Master Data version. Missing session snapshots do not fall back to the current Active package.

Option validation counts actual selected quantities. It rejects duplicate option rows, unknown groups, non-member Options, selections below/above Group limits, and repeated quantities that violate `allowRepeatedOption` or `maxQuantityPerOption`. Any error produces no partially resolved selection.

## SQLite schema version 5

Schema version 5 adds:

- `master_data_sellable_items`, versioned by branch and Master Data package
- `master_data_option_groups` and `master_data_option_group_items`
- `branch_sold_out_overrides`, which is deliberately outside package ownership
- `branch_sold_out_audit`, reserved for immutable Sold Out transition history

The package tables allow a missing `resolved_price` because D08.3 defines that state as not sellable. Zero is valid. Negative prices are rejected by the application boundary; the future package activation workflow must validate the complete package before it becomes Active.

## Deliberately outside this slice

- package download, checksum verification, publish validation, and atomic activation commands
- a production package writer or importer
- commands and permission checks for setting or clearing Sold Out, including audit writes
- `SubmitOrder`, Order Batch persistence, production print jobs, Recipe snapshots, and Stock Movements
- public Branch API endpoints and Mobile/PC client contracts
- production-station routing and menu category/search presentation metadata

`SqliteCatalogSnapshotResolver` is the authoritative persistence adapter prepared for the next `SubmitOrder` slice. This change does not make ordering available by itself.
