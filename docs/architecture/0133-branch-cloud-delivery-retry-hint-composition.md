# Branch Cloud delivery retry-hint composition

The Branch Cloud outbox processor now combines an advisory delivery retry hint
with its existing bounded exponential backoff. The scheduled delay is the
longer of the locally calculated delay and the server hint, capped by the
configured maximum retry delay.

This prevents a remote response from accelerating delivery below the Branch's
local backoff or postponing delivery beyond the locally configured bound.
Transport exceptions, acknowledgment mismatches, and failures without a hint
continue to use only the local retry policy. The resulting next-attempt time is
persisted through the existing delivery-failure store.
