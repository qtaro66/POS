import type { ClosingReportQuery } from '../api/closingReportWorkspaceContracts.ts'

export type ReportInvestigationTarget = 'all' | 'base-pending' | 'lifecycle-pending'
export interface ReportInvestigationRequest { target: ReportInvestigationTarget; revision: number }

export function reportInvestigationQuery(target: ReportInvestigationTarget): ClosingReportQuery {
  return target === 'base-pending' ? { status: 'Pending', limit: 20 }
    : target === 'lifecycle-pending' ? { status: 'All', lifecycleStatus: 'Pending', limit: 20 }
      : { status: 'All', limit: 20 }
}
