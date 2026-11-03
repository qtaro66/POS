import { useEffect, useMemo, useSyncExternalStore } from 'react'
import { createPrintAttention, type PrintAttentionPort } from './printAttention.ts'
import { FreshnessValue } from './DataFreshnessPanel.tsx'

export function HomePrintAttention({ port, openDetails }: { port: PrintAttentionPort; openDetails: () => void }) {
  const controller = useMemo(() => createPrintAttention(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  return <section aria-labelledby="home-print-attention-title">
    <h3 id="home-print-attention-title">งานพิมพ์ที่ต้องตรวจสอบ</h3>
    <p role="status">{state.page ? <>
      <strong>{state.page.totalCount}</strong> งาน · เวลาอ้างอิง <FreshnessValue reading={{ phase: 'ready', timestamp: state.page.observedAt }} />
    </> : ({
      loading: 'กำลังอ่านจำนวนงานพิมพ์',
      unavailable: 'อ่านจำนวนงานพิมพ์ไม่ได้',
      unsupported: 'ยังไม่เชื่อมต่อรายการงานพิมพ์',
      ready: 'ยังไม่ทราบจำนวนงานพิมพ์',
    })[state.phase]}</p>
    {port.printAttention && <button className="refresh-button" onClick={openDetails}>เปิดรายละเอียดงานพิมพ์</button>}
  </section>
}
