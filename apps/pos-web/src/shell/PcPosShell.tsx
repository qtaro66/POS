import { useEffect, useRef, useState, type ReactNode } from 'react'
import { strings as s } from '../ordering/strings.ts'
import './PcPosShell.css'

const pages = [
  { id: 'home', label: 'หน้าหลัก', icon: '⌂' },
  { id: 'tables', label: 'โต๊ะ', icon: '▦' },
  { id: 'history', label: 'ประวัติธุรกรรม', icon: '▤' },
  { id: 'store', label: 'ร้านและกะ', icon: '◷' },
  { id: 'system', label: 'สถานะระบบ', icon: '⚙' },
] as const
type PageId = typeof pages[number]['id']

export function PcPosShell({ branchLabel, preview, onLock, children, sidebarStatus, systemPage, homePage }: {
  branchLabel: string; preview: boolean; onLock: () => void; children: ReactNode; sidebarStatus?: ReactNode; systemPage?: ReactNode
  homePage?: (openTables: () => void, openSystem: () => void) => ReactNode
}) {
  const [page, setPage] = useState<PageId>('tables')
  const main = useRef<HTMLElement>(null), moveFocus = useRef(false)
  useEffect(() => {
    if (moveFocus.current) { main.current?.focus(); moveFocus.current = false }
  }, [page])
  const selected = pages.find(value => value.id === page)!
  function navigate(next: PageId) {
    if (page === next) return
    moveFocus.current = true
    setPage(next)
  }
  return <div className="pos-shell">
    <header className="pos-topbar"><div><span className="brand-mark" aria-hidden="true">▣</span><strong>{branchLabel}</strong><span className="topbar-divider" /><span>PC POS</span></div>
      <button className="lock-button" onClick={onLock}>{s.lock}</button></header>
    {preview && <div className="preview-banner" role="note">{s.preview}</div>}
    <div className="pos-layout">
      <aside className="pos-sidebar"><h1>{s.appTitle}</h1><nav aria-label="เมนูหลัก">
        {pages.map(value => <button key={value.id} type="button" aria-current={page === value.id ? 'page' : undefined}
          onClick={() => navigate(value.id)}>
          <span aria-hidden="true">{value.icon}</span>{value.label}</button>)}
      </nav>{sidebarStatus ?? <p>ข้อมูลจากบริการสาขา<br />ตามเวลาที่อ่านล่าสุด</p>}</aside>
      <main className="ordering-main" ref={main} tabIndex={-1} aria-label={selected.label}>
        {page === 'tables' ? children : page === 'home' && homePage ? homePage(() => navigate('tables'), () => navigate('system')) : page === 'system' && systemPage ? systemPage : <section className="shell-unavailable panel" aria-labelledby="shell-page-title">
          <p className="eyebrow">PC POS / {selected.label}</p><h2 id="shell-page-title">{selected.label}</h2>
          <p role="status">หน้านี้ยังไม่เชื่อมต่อข้อมูลและคำสั่ง จึงยังไม่พร้อมใช้งาน</p>
          <p>เลือกโต๊ะจากเมนูหลักเพื่อกลับไปยังงานเดิม</p>
        </section>}
      </main>
    </div>
  </div>
}
