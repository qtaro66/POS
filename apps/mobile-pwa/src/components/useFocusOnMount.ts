import { useEffect, useRef } from 'react'

/** Moves keyboard and screen-reader focus to a screen's heading when the screen appears. */
export function useFocusOnMount<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  useEffect(() => {
    ref.current?.focus()
  }, [])
  return ref
}
