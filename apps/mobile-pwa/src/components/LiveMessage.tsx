import { useEffect, useState } from 'react'

/**
 * Polite live region for confirmations such as "added to the basket". The
 * text is filled in just after mount, so it is announced even when the
 * screen that shows it has just appeared.
 */
export function LiveMessage({ message }: { readonly message: string | null }) {
  const [shown, setShown] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => setShown(message ?? ''), 0)
    return () => clearTimeout(timer)
  }, [message])

  return (
    <p className="live-message" aria-live="polite">
      {shown}
    </p>
  )
}
