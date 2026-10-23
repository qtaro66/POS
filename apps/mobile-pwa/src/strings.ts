import type { TableSessionStatus } from './domain/types.ts'

/*
 * User-facing text, kept in one module so it can be localized without touching
 * screens. The requirements do not yet confirm Thai wording for these Mobile
 * screens, so no Thai labels are introduced here.
 */
export const text = {
  appName: 'POS Mobile',

  pairingTitle: 'Pair this device',
  pairingIntro:
    'Ask a signed-in POS user to open a pairing QR code on the PC POS, then scan it here.',
  pairingRules:
    'Each device needs its own QR code. A code works once and expires after 5 minutes.',
  deviceNameLabel: 'Device name',
  deviceNameRequired: 'Enter a name for this device.',
  pairingScan: 'Scan pairing QR code',
  pairingInProgress: 'Pairing with the branch…',
  pairingScanCancelled: 'Scanning was cancelled. Try again when the QR code is ready.',
  pairingScannerUnavailable:
    'The camera is not available. Allow camera access for this app and try again.',
  pairingMalformed: 'This QR code is not a valid POS pairing code. Ask for a new QR code.',
  pairingInsecure:
    'This pairing code does not use a secure HTTPS branch address, so it cannot be used.',
  pairingUnsupportedVersion:
    'This pairing code was made by a different POS version. Update this app or ask for a new QR code.',
  pairingExpired: 'This pairing code has expired. Ask for a new QR code.',
  pairingAlreadyUsed: 'This pairing code has already been used. Ask for a new QR code.',
  pairingInvalid:
    'The branch did not accept this pairing code. It may have expired or already been used. Ask for a new QR code.',
  pairingInvalidRequest:
    'The branch could not accept this pairing request. Check the device name and try again.',
  pairingRateLimited: 'Too many pairing attempts. Wait a moment, then try again.',
  pairingUnreachable:
    'The branch service cannot be reached. Check the shop network and try again.',
  pairingFailed: 'Pairing did not complete because of an unexpected error. Try again.',

  loginTitle: 'Employee login',
  loginDeviceLine: (deviceName: string, branchName: string) =>
    `${deviceName} · ${branchName}`,
  loginPinLabel: 'PIN',
  loginSubmit: 'Log in',
  loginInProgress: 'Checking PIN…',
  loginPinRequired: 'Enter your PIN.',
  loginRejected: 'The PIN was not accepted. Try again.',
  loginUnreachable:
    'The branch service cannot be reached, so the PIN cannot be checked. Try again when the branch is online.',
  loginFailed: 'Login did not complete because of an unexpected error. Try again.',
  sessionExpired: 'Your session has expired. Enter your PIN to log in again.',
  unsentOnDevice: (count: number) =>
    count === 1
      ? '1 unsent item is saved on this device.'
      : `${count} unsent items are saved on this device.`,

  tablesTitle: 'Active tables',
  signedInAs: (name: string) => `Signed in as ${name}`,
  logout: 'Log out',
  refresh: 'Refresh tables',
  refreshing: 'Refreshing…',
  tablesLoading: 'Loading tables…',
  tablesEmpty: 'No tables are open at this branch.',
  tablesUnreachable: 'The table list could not be refreshed from the branch.',
  tablesFailed: 'The table list could not be loaded because of an unexpected error.',
  tablesShowingLast: 'Showing the last list received from the branch.',
  tablesReceivedAt: 'Received',
  tableLabel: (label: string) => `Table ${label}`,
  tableOpenedAt: 'Opened',
  tableNotListed: (label: string) =>
    `Table ${label} is not in the last list received from the branch. It may have been closed or moved.`,
  tableDetailsUnavailable:
    'Order lines the branch has accepted and bill totals are not shown on this device yet.',

  addItems: 'Add items',
  addItemsNotOrdering:
    'Items can be added only while the table is Ordering, according to the last list received from the branch.',
  basketHeading: 'Basket',
  basketEmpty: 'The basket is empty.',
  basketCount: (count: number) =>
    count === 1 ? '1 item in the basket' : `${count} items in the basket`,
  basketMemoryOnly: 'The basket is kept only while this app stays open.',
  basketSavedOnDevice: 'The basket is saved on this device.',
  basketSaving: 'Saving the basket on this device…',
  basketNotSaved: 'The latest basket changes are not saved on this device.',
  basketNotSentYet: 'Sending orders to the branch is not available yet.',
  basketNeedsReview:
    'This basket was started more than 30 days ago and needs review before it can be sent.',
  basketSaveFailed: 'The last change to the basket could not be stored.',
  basketDecrease: (name: string) => `Decrease ${name}`,
  basketIncrease: (name: string) => `Increase ${name}`,
  basketQuantity: (name: string) => `Quantity of ${name}`,
  basketEdit: 'Edit',
  basketEditLabel: (name: string) => `Edit ${name}`,
  basketRemove: 'Remove',
  basketRemoveLabel: (name: string) => `Remove ${name}`,
  basketRemoved: (name: string) => `${name} removed from the basket.`,
  basketUpdated: (name: string) => `${name} updated in the basket.`,
  basketItemUnavailable: 'No longer available. Remove it or choose another item.',
  basketOptionsInvalid: 'The chosen options are no longer valid. Edit this item.',
  basketUnitPrice: 'Unit price',
  basketComment: 'Note',

  menuTitle: (tableLabel: string) => `Menu · Table ${tableLabel}`,
  menuCategories: 'Categories',
  menuAllCategories: 'All',
  menuLoading: 'Loading the menu…',
  menuUnreachable: 'The menu could not be loaded from the branch.',
  menuFailed: 'The menu could not be loaded because of an unexpected error.',
  menuRetry: 'Try again',
  menuSoldOut: 'Sold out',
  menuHasOptions: 'Options',
  menuAdded: (name: string, count: number) =>
    `${name} added. ${count === 1 ? '1 item' : `${count} items`} in the basket.`,
  menuViewBasket: (count: number) => `View basket (${count})`,
  menuItemMissing: 'This item is not in the menu any more.',

  itemQuantity: 'Quantity',
  itemDecrease: 'Decrease quantity',
  itemIncrease: 'Increase quantity',
  itemComment: 'Note for the kitchen',
  itemAdd: 'Add to basket',
  itemUpdate: 'Update basket',
  itemFixProblems: 'Check the highlighted choices.',
  itemLineMissing: 'This basket item no longer exists.',
  itemUnavailableTitle: 'Item not available',
  groupRule: (min: number, max: number) =>
    min === 0
      ? `Optional · choose up to ${max}`
      : min === max
        ? `Required · choose ${min}`
        : `Required · choose ${min} to ${max}`,
  groupChosen: (count: number, max: number) => `${count} of ${max} chosen`,
  groupBelowMinimum: (min: number) => (min === 1 ? 'Choose at least 1.' : `Choose at least ${min}.`),
  groupAboveMaximum: (max: number) => `Choose no more than ${max}.`,
  groupOptionLimit: 'One option is chosen more times than allowed.',
  groupUnavailable: 'A chosen option is no longer available.',
  optionDecrease: (name: string) => `Remove one ${name}`,
  optionIncrease: (name: string) => `Add one ${name}`,
  optionQuantity: (name: string) => `${name} quantity`,

  documentTitle: (screen: string) => `${screen} · POS Mobile`,
  navLabel: 'Main',
  navTables: 'Tables',
  navStatus: 'Status',

  statusTitle: 'Status and outbox',
  statusConnectionHeading: 'Connection',
  statusBranch: 'Branch service',
  statusEmployeeData: 'Employee data synced',
  outboxHeading: 'Unsent items on this device',
  outboxState: {
    draft: 'Draft',
    pending: 'Pending',
    submitted: 'Submitted',
    unknown: 'Result unknown',
  },
  statusDeviceHeading: 'This device',
  statusDeviceName: 'Device name',
  statusBranchName: 'Branch',
  statusEmployee: 'Signed in as',

  statusRegion: 'Connection and sync status',
  branchOnline: 'Branch online',
  branchOffline: 'Branch offline',
  offlineNotice:
    'Orders cannot be sent while the branch is offline. Employee changes made centrally may not have arrived yet.',
  employeeDataSynced: 'Employee data synced',
  employeeDataNeverSynced: 'Employee data not yet synced',
  outboxClear: 'No unsent items',
  outboxCount: (count: number) => (count === 1 ? '1 unsent item' : `${count} unsent items`),
} as const

export const tableStatusText: Record<TableSessionStatus, string> = {
  Ordering: 'Ordering',
  PaymentPending: 'Payment pending',
  PaymentInProgress: 'Payment in progress',
  PartiallyPaid: 'Partially paid',
  Completed: 'Completed',
}
