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
