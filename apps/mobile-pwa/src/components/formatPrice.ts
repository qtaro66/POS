import type { MinorAmount } from '../domain/menu.ts'

const baht = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'THB' })

/** Formats a Branch-resolved price in satang; Mobile never derives prices itself. */
export function formatPrice(amount: MinorAmount): string {
  return baht.format(amount / 100)
}
