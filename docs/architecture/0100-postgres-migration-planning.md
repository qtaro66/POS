# 0100: PostgreSQL migration planning and drift detection

## Status

Accepted

## Decision

Before executing any Central PostgreSQL migration, compare the database's
recorded migration history with the ordered embedded catalog. Applied migrations
must form an exact, case-sensitive prefix of the catalog with unique positive
versions and unique non-empty names.

An empty database receives the complete catalog as its pending plan. A current
database receives an empty plan. A renamed, missing, duplicated, or future
migration fails closed as schema drift rather than being skipped or silently
repaired.

The returned pending plan is read-only. A future executor may apply only that
plan and must re-evaluate it while holding its database migration lock.

## Boundaries

This slice does not connect to PostgreSQL, inspect the migration table, acquire
a database lock, open a transaction, execute SQL, or update schema history.

Automated tests use in-memory migration identities and contact no database,
network, or Branch hardware.
