import { useEffect, useId, type ReactNode } from 'react'
import { text } from '../strings.ts'
import { useFocusOnMount } from './useFocusOnMount.ts'

interface ScreenProps {
  readonly title: string
  readonly busy?: boolean
  /** Shown next to the heading, for example the signed-in employee or a status badge. */
  readonly subtitle?: ReactNode
  readonly actions?: ReactNode
  readonly children?: ReactNode
}

/**
 * One screen of the authenticated shell. When it appears, focus moves to its
 * heading so screen readers announce the new screen, and the document title
 * names it. Remount the screen (with a `key`) to repeat this on navigation.
 */
export function Screen({ title, busy = false, subtitle, actions, children }: ScreenProps) {
  const headingRef = useFocusOnMount<HTMLHeadingElement>()
  const headingId = useId()

  useEffect(() => {
    document.title = text.documentTitle(title)
  }, [title])

  return (
    <main className="screen" aria-busy={busy} aria-labelledby={headingId}>
      <div className="screen__header">
        <div className="screen__heading">
          <h1 ref={headingRef} id={headingId} tabIndex={-1}>
            {title}
          </h1>
          {subtitle}
        </div>
        {actions}
      </div>
      {children}
    </main>
  )
}
