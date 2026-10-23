import { useCallback, useEffect, useRef, useState } from 'react'
import type { DraftRepository } from '../adapters/ports.ts'
import {
  applyDraftExpiry,
  emptyDraft,
  touchDraft,
  type Draft,
  type DraftKey,
} from '../domain/draft.ts'

export interface DraftData {
  /** Stored basket of the current table session; null when there is none yet. */
  readonly draft: Draft | null
  /** True once the repository has been read for the current table session. */
  readonly loaded: boolean
  /** True while a change is being written to the repository. */
  readonly saving: boolean
  /** True when the last save to the repository failed. */
  readonly saveFailed: boolean
  /**
   * Applies a change. A table without a basket gets an empty one bound to
   * `sessionMasterDataVersion`, the version of the menu the change came from;
   * without that version a new basket cannot be started and the change is
   * ignored.
   */
  readonly update: (
    change: (draft: Draft) => Draft,
    sessionMasterDataVersion: string | null,
  ) => void
}

interface Held {
  /** Identifies the table session (and device and employee) the Draft belongs to. */
  readonly scope: string
  readonly draft: Draft | null
}

function scopeOf(key: DraftKey | null): string | null {
  return key ? `${key.deviceId}\u0000${key.employeeId}\u0000${key.tableSessionId}` : null
}

/**
 * The basket (Draft) of the current table session on this device. Every
 * change updates the screen at once and is then written to the repository.
 */
export function useDraft(
  repository: DraftRepository,
  key: DraftKey | null,
  now: () => Date,
): DraftData {
  const scope = scopeOf(key)
  const [held, setHeld] = useState<Held | null>(null)
  const [saveFailed, setSaveFailed] = useState(false)
  const [pendingSaves, setPendingSaves] = useState(0)
  // Latest value, so consecutive updates in one event build on each other.
  const heldRef = useRef<Held | null>(null)
  const deviceId = key?.deviceId
  const employeeId = key?.employeeId
  const tableSessionId = key?.tableSessionId

  const persist = useCallback(
    (draft: Draft) => {
      setPendingSaves((count) => count + 1)
      repository.save(draft).then(
        () => setSaveFailed(false),
        () => setSaveFailed(true),
      ).finally(() => setPendingSaves((count) => count - 1))
    },
    [repository],
  )

  useEffect(() => {
    if (!scope || !deviceId || !employeeId || !tableSessionId) return
    let active = true
    const store = (stored: Draft | null) => {
      if (!active) return
      // A Draft older than the retention period is marked for review when it is opened.
      const draft = stored && applyDraftExpiry(stored, now())
      heldRef.current = { scope, draft }
      setHeld(heldRef.current)
      if (draft && draft !== stored) persist(draft)
    }
    // An unreadable store starts without a basket instead of blocking the table.
    repository.load({ deviceId, employeeId, tableSessionId }).then(store, () => store(null))
    return () => {
      active = false
    }
  }, [deviceId, employeeId, now, persist, repository, scope, tableSessionId])

  const update = useCallback(
    (change: (draft: Draft) => Draft, sessionMasterDataVersion: string | null) => {
      if (!scope || !deviceId || !employeeId || !tableSessionId) return
      const latest = heldRef.current
      if (!latest || latest.scope !== scope) return
      const at = now()
      const current =
        latest.draft ??
        (sessionMasterDataVersion
          ? emptyDraft({ deviceId, employeeId, tableSessionId }, sessionMasterDataVersion, at)
          : null)
      if (!current) return
      const next = touchDraft(change(current), at)
      heldRef.current = { scope, draft: next }
      setHeld(heldRef.current)
      persist(next)
    },
    [deviceId, employeeId, now, persist, scope, tableSessionId],
  )

  const current = held && held.scope === scope ? held : null
  return {
    draft: current?.draft ?? null,
    loaded: current !== null,
    saving: pendingSaves > 0,
    saveFailed,
    update,
  }
}
