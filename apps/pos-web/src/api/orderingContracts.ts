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

export interface BillPreviewTotals {
  readonly masterDataVersion: string; readonly calculationPolicyVersion: string
  readonly vatRate: number; readonly serviceChargeRate: number
  readonly itemGrossAmount: number; readonly lineDiscountAmount: number; readonly itemNetAmount: number; readonly billDiscountAmount: number
  readonly serviceChargeBase: number; readonly serviceChargeAmount: number; readonly totalBeforeRounding: number
  readonly roundingAdjustment: number; readonly netTotal: number; readonly vatInclusiveAmount: number; readonly amountBeforeVat: number
}
export interface AcceptedLine {
  readonly orderLineId: string; readonly lineSequence: number; readonly sellableItemId: string; readonly displayName: string
  readonly originalQuantity: number; readonly activeQuantity: number; readonly status: 'Active' | 'Cancelled'
  readonly unitPrice: number; readonly fulfillmentType: FulfillmentType; readonly comment: string | null
  readonly options: readonly { readonly sellableItemId: string; readonly optionGroupId: string | null; readonly displayName: string; readonly quantityPerItem: number; readonly unitPrice: number }[]
  readonly activeGrossAmount: number; readonly activeDiscountAmount: number; readonly activeNetAmount: number
}
export interface AcceptedBatch {
  readonly orderBatchId: string; readonly requestId: string; readonly branchSequence: number | null
  readonly serverReceivedAt: string; readonly acceptanceStatus: 'BranchAccepted'; readonly lines: readonly AcceptedLine[]
  readonly activeGrossAmount: number; readonly activeLineDiscountAmount: number; readonly activeItemNetAmount: number
}
export interface SessionDetail {
  readonly schemaVersion: 1
  readonly session: ActiveTables['sessions'][number]
  readonly asOf: string
  readonly hasOpenCancellationRequest: boolean
  readonly billPreviewTotals: BillPreviewTotals | null
  readonly batches: readonly AcceptedBatch[]
}
