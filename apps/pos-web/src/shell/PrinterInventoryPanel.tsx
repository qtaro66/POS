import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { createPrinterInventory, type PrinterInventoryPort } from './printerInventory.ts'
import './PrinterInventoryPanel.css'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
function Stamp({ value }: { value: string }) { return <time dateTime={value}>{clock.format(new Date(value))}</time> }
export function PrinterInventoryPanel({ port }: { port: PrinterInventoryPort }) {
  const controller = useMemo(() => createPrinterInventory(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const list = useRef<HTMLDivElement>(null), focus = useRef<{ owner: typeof controller } | null>(null)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  useEffect(() => {
    if (state.phase !== 'loading' && focus.current) {
      if (focus.current.owner === controller && state.phase === 'ready') list.current?.focus()
      focus.current = null
    }
  }, [state, controller])
  const page = state.page
  return <section className="printer-inventory" aria-labelledby="printer-inventory-title" aria-busy={state.phase === 'loading'}>
    <header><h3 id="printer-inventory-title">ทะเบียนเครื่องพิมพ์</h3><button className="refresh-button" disabled={state.phase === 'loading'} onClick={() => { void controller.refresh() }}>อ่านทะเบียนเครื่องพิมพ์ใหม่</button></header>
    <p>อ่านค่าที่ตั้งไว้จากฐานข้อมูลเท่านั้น ไม่เชื่อมต่อหรือทดสอบเครื่องพิมพ์ ไม่เปิดลิ้นชัก และไม่แก้ Profile</p>
    {state.phase === 'unsupported' && <p role="status">ยังไม่เชื่อมต่อทะเบียนเครื่องพิมพ์</p>}
    {state.phase === 'loading' && <p role="status">กำลังอ่านทะเบียนเครื่องพิมพ์</p>}
    {state.phase === 'unavailable' && <p role="alert">อ่านทะเบียนเครื่องพิมพ์ไม่ได้ กรุณาอ่านใหม่เพื่อกลับหน้าแรก</p>}
    {page && <>
      <p>ตั้งค่าไว้ทั้งหมด: <strong>{page.totalCount}</strong> · Active ตามค่าที่ตั้ง: {page.activeCount} · หน้าที่ {state.pageNumber} · แสดง {page.items.length} รายการ</p>
      <p>อ่านข้อมูลเมื่อ: <Stamp value={page.observedAt} /> (ไม่ใช่เวลา Health Check) · ข้อมูลอาจเปลี่ยนระหว่างหน้า</p>
      {page.items.length === 0 && <p role="status">ไม่พบเครื่องพิมพ์ในหน้าที่อ่าน ไม่ยืนยันว่าทุกเครื่องออนไลน์หรือพร้อมใช้งาน</p>}
      <div ref={list} className="printer-inventory-list" role="region" aria-label="รายการทะเบียนเครื่องพิมพ์" tabIndex={0}>
        {page.items.map(item => <article key={item.printerId} className="printer-inventory-item" aria-label={`เครื่องพิมพ์ ${item.printerId}`}>
          <h4>{item.displayName} · {item.printerId}</h4>
          <p>สถานะอุปกรณ์: <strong>Unknown</strong> — ยังไม่ได้ตรวจเครื่องจริง</p>
          <dl><div><dt>สถานี / เปิดใช้ตามค่า</dt><dd>{item.productionStationId} / {item.isActive ? 'Active' : 'Inactive'}</dd></div>
            <div><dt>IP / Hostname / Port</dt><dd>{item.host ?? 'ไม่แสดงปลายทางที่รูปแบบไม่รองรับ'} / {item.port}</dd></div>
            <div><dt>Protocol / Profile</dt><dd>{item.protocol} / {item.compatibilityProfileId}</dd></div>
            <div><dt>ผู้ผลิต / รุ่น</dt><dd>{item.manufacturer} / {item.model}</dd></div><div><dt>Product Code / Firmware</dt><dd>{item.productCode ?? 'ไม่มีบันทึก'} / {item.firmwareVersion ?? 'ไม่มีบันทึก'}</dd></div>
            <div><dt>กระดาษ / ลิ้นชักตามค่า</dt><dd>{item.paperWidthMillimeters} mm / {item.supportsCashDrawer ? 'ตั้งค่าให้รองรับ' : 'ไม่ได้ตั้งค่าให้รองรับ'} (ยังไม่ทดสอบ)</dd></div>
            <div><dt>โหมดสถานะที่ตั้งไว้</dt><dd>{item.statusMode} (ไม่ใช่ผลตรวจอุปกรณ์)</dd></div>
            <div><dt>แก้ไขค่าล่าสุด</dt><dd><Stamp value={item.updatedAt} /></dd></div></dl>
        </article>)}
      </div>
      {page.nextAfterPrinterId && <button className="refresh-button" onClick={() => { focus.current = { owner: controller }; void controller.next() }}>ทะเบียนเครื่องพิมพ์หน้าถัดไป</button>}
    </>}
  </section>
}
