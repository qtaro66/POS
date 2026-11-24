# POS report investigation navigation

This workflow completes read-only investigation from the system delivery
overview to the existing saved closing reports. It advances D18.9 and D04.11.7
without changing closing commands, report snapshots or Central ingestion.

## Entry points and scope

The main navigation provides a dedicated closing-reports page. It uses only the
authenticated report list/detail read capabilities and does not require the
store lifecycle command capability. The existing store-and-shift report browser
and links from the shift timeline remain available.

System status exposes separate investigation actions for pending original
reports and pending opening/store/shift companions. Actions require a ready
modern delivery overview, a positive corresponding pending count and a live
report navigation capability. A disabled sender does not hide pending work.
Loading, unavailable and unsupported observations do not offer stale actions.
Table-event and replay counts are not presented as closing-report work.

The overview count is an observation, not a promise about the next page. Queue
membership can change before the report query executes, including becoming
empty. Navigation always issues a fresh authenticated Branch read.

## Investigation requests

Internal requests contain a target and revision. All opens the normal first
page. Base-pending uses status=Pending. Lifecycle-pending uses status=All and
lifecycleStatus=Pending, so a confirmed base packet with an unconfirmed companion
remains visible. All targets use the existing bounded report page size.

Each navigation action creates a fresh revision. Report filters, selection and
continuation state are reset for that request; users may then edit or clear the
filters normally. An old page or detail response cannot populate a newer request
or a replacement scope. The report heading receives keyboard focus on entry;
the shell retains its normal main-region fallback.

Report intent is owned by the authenticated client generation, including when a
replacement client has the same branch scope. Replacement cancels old report
reads and starts All for the new owner. It does not reset unrelated system,
printer or outbox navigation; their existing identity replacement behavior is
preserved.

No investigation intent is persisted as a command or stored with pending store
lifecycle requests. Navigation does not invoke close, retry, retire, print,
export or device-test operations. Leaving reports does not discard the current
table workspace or replace accepted sale amounts.

## Compatibility and validation

The shell retains its existing React-node page props while allowing system
status to receive the navigation callback. Missing read capabilities and preview
mode do not expose operational report reads or pending investigation actions;
the ordinary navigation placeholder may remain visible. Existing HTTP routes, query
values, cursor version 2, response metadata, Branch schema 35 and Central schema
3 are unchanged.

Component and application integration tests cover target mapping, capability
gates, repeated entry, focus, editable/reset filters, independent receipts,
loading/unavailable observations, late responses and session/scope replacement.
These are browser-model tests using synthetic Branch responses; they are not
deployed PostgreSQL, mTLS or hardware evidence.
