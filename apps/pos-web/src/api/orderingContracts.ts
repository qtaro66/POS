/** Mirrors the opt-in Branch ordering v1 DTOs; no client price calculations. */
export type FulfillmentType = 'DineIn' | 'Takeaway'
export type SessionStatus = 'Ordering' | 'PaymentPending' | 'PaymentInProgress' | 'PartiallyPaid' | 'Completed' | 'Cancelled' | 'Closed'
export interface OrderOption { readonly optionGroupId: string; readonly sellableItemId: string; readonly quantity: number }
export interface OrderLine {
  readonly sellableItemId: string
  readonly quantity: number
  readonly fulfillmentType: FulfillmentType
  readonly comment?: string | null
  readonly options?: readonly OrderOption[]
}
export interface CommandResponse {
  readonly result: {
    readonly schemaVersion: 1
    readonly requestId: string
    readonly status: 'BranchAccepted' | 'Rejected' | 'NoDurableResult'
    readonly tableSessionId: string | null
    readonly tableSessionStatus: SessionStatus | null
    readonly tableVersion: number | null
    readonly errorCode: string | null
    readonly serverReceivedAt: string | null
    readonly branchSequence: number | null
  }
  readonly validationDetails: unknown
}
export interface ActiveTables {
  readonly schemaVersion: 1
  readonly includesAvailableTables: false
  readonly sessions: readonly { readonly tableSessionId: string; readonly tableId: string; readonly status: SessionStatus; readonly tableVersion: number }[]
}
export interface MenuItem {
  readonly sellableItemId: string
  readonly kind: 'Item' | 'SubItem' | 'Topping'
  readonly displayName: string
  readonly unitPrice: number | null
  readonly availability: { readonly existsInActivePackage: boolean; readonly centralActive: boolean; readonly branchEnabled: boolean; readonly hasCurrentPrice: boolean; readonly soldOut: boolean }
  readonly optionGroups: readonly {
    readonly optionGroupId: string
    readonly displayName: string
    readonly optionType: 'SubItem' | 'Topping'
    readonly minSelections: number
    readonly maxSelections: number
    readonly allowRepeatedOption: boolean
    readonly maxQuantityPerOption: number
    readonly options: readonly MenuItem[]
  }[]
}
export interface SessionMenu { readonly schemaVersion: 1; readonly tableSessionId: string; readonly masterDataVersion: string; readonly items: readonly MenuItem[] }
