const timeFormat: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }

/** Displays a timestamp as local clock time while keeping the exact value machine-readable. */
export function Time({ value }: { readonly value: string }) {
  return <time dateTime={value}>{new Date(value).toLocaleTimeString(undefined, timeFormat)}</time>
}
