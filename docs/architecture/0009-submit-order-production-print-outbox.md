# ADR 0009: Production routing and persistent print outbox for SubmitOrder

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D01.4.1, D03, D09, D12.5, D13, and D20
- Extends: ADR 0008

## Decision

An accepted `SubmitOrder` resolves the production station from the table session's frozen Master Data version. The Branch snapshots that station onto the immutable Order Line. A later Master Data change therefore cannot redirect a retry or future line-level reprint of the original kitchen order.

Within the same serialized SQLite transaction, the Branch:

1. persists the Order Batch, Order Lines, and Option snapshots
2. groups routed Order Lines by exact, case-sensitive `productionStationId`
3. creates one immutable production-order payload per station and Order Batch
4. creates one persistent `KitchenOrder` print job per payload in `PrintQueued`
5. persists the cloud-sync outbox and accepted command receipt

Items with no Production Station remain valid sale lines but do not produce a kitchen/production print job. Options and Toppings follow the parent Order Line and cannot route independently in this version.

The payload preserves the original table, fulfillment type, quantity, comment, Options/Toppings, Order Line identifier, Order Batch identifier, station, and Branch submission time. It is serialized once, hashed with SHA-256, and never rebuilt from current Master Data for retry or reprint.

`print_jobs.target_station` is now a validated station identifier rather than a closed enum. `Cashier` remains the reserved target for Bill Preview jobs. Database triggers require a Bill Preview job to reference a Bill Preview snapshot and a Kitchen Order job to reference a production snapshot with the same target station.

## SQLite schema version 7

Schema version 7 adds:

- nullable `production_station_id` to frozen Master Data sellable items
- nullable `production_station_id` to immutable Order Lines
- `production_order_snapshots` with versioned JSON and SHA-256
- `KitchenOrder` as a print-job purpose
- arbitrary non-empty production station identifiers
- insert/update guards between print-job purpose, snapshot type, and station

The migration preserves existing Bill Preview snapshots and print jobs. Existing Master Data and Order Lines receive no production route and therefore retain their previous behavior.

## Safety and failure behavior

Automated tests operate only on in-memory or temporary SQLite data and never contact a printer. If snapshot or print-job persistence fails before commit, the entire `SubmitOrder` transaction rolls back. Failures after a print dispatcher begins sending remain governed by D20: an uncertain result is `Unknown` and must not be retried automatically.

## Deliberately outside this slice

- public Branch API endpoint and transport contract for `SubmitOrder`
- station-to-printer device configuration and dispatcher selection
- ESC/POS rendering of the immutable production payload
- print attempt processing, ACK/status handling, retry scheduling, and operator reprint command
- Recipe snapshots and Stock Movements while D07.2-D07.3 remain deferred
- a separate immutable SubmitOrder business-audit event
- employee/device authentication and pairing enforcement at the API boundary
