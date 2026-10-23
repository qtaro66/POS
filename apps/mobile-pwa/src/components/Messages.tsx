import type { ReactNode } from 'react'

/** A problem the user should notice now; announced immediately by screen readers. */
export function Alert({ children }: { readonly children: ReactNode }) {
  return (
    <div role="alert" className="alert">
      {children}
    </div>
  )
}

/** Information that does not need immediate attention, such as a missing feature. */
export function Notice({ children }: { readonly children: ReactNode }) {
  return <div className="notice">{children}</div>
}
