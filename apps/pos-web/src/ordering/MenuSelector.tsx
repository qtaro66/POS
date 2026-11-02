import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react'
import type { MenuItem, OrderOption, SessionMenu } from '../api/orderingContracts.ts'
import type { createBasketEditor } from './basketEditor.ts'
import { basketSelectionProblem, isMenuItemAvailable } from './basketSubmission.ts'

type Group = MenuItem['optionGroups'][number]
type Selection = { owner: ReturnType<typeof createBasketEditor>; menu: SessionMenu; item: MenuItem; options: OrderOption[]; stage: 'subitem' | 'topping' }
const money = new Intl.NumberFormat('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 20 })
function validGroups(groups: readonly Group[], options: readonly OrderOption[]) {
  return groups.every(group => {
    const count = options.filter(option => option.optionGroupId === group.optionGroupId).reduce((sum, option) => sum + option.quantity, 0)
    return count >= group.minSelections && count <= group.maxSelections
  })
}

function Choices({ groups, options, change, disabled }: {
  groups: readonly Group[]; options: readonly OrderOption[]; change: (group: Group, item: MenuItem, quantity: number) => void; disabled: boolean
}) {
  const id = useId()
  return groups.map(group => <fieldset key={group.optionGroupId} disabled={disabled}><legend>{group.displayName}</legend>
    <p className="selection-rule">เลือก {group.minSelections}–{group.maxSelections} รวมตามจำนวน</p>
    {group.options.map(item => {
      const quantity = options.find(option => option.optionGroupId === group.optionGroupId && option.sellableItemId === item.sellableItemId)?.quantity ?? 0
      return <label className="option-choice" key={item.sellableItemId}>
        {group.allowRepeatedOption
          ? <input type="number" aria-label={`จำนวน ${item.displayName}`} min={0} max={group.maxQuantityPerOption} step={1} value={quantity} disabled={!isMenuItemAvailable(item)}
            onChange={event => change(group, item, Number(event.target.value))} />
          : <input type={group.maxSelections === 1 ? 'radio' : 'checkbox'} name={`${id}-${group.optionGroupId}`} checked={quantity > 0} disabled={!isMenuItemAvailable(item)}
            onChange={event => change(group, item, event.target.checked ? 1 : 0)} />}
        <span>{item.displayName}{!isMenuItemAvailable(item) && <small> / ไม่พร้อมขาย</small>}</span><span>{item.unitPrice === null ? '—' : money.format(item.unitPrice)}</span>
      </label>
    })}
    {group.minSelections === 0 && <button type="button" className="clear-options" onClick={() => { for (const item of group.options) change(group, item, 0) }}>ล้างตัวเลือกกลุ่มนี้</button>}
  </fieldset>)
}

export function MenuSelector({ controller, onBeginSelection }: { controller: ReturnType<typeof createBasketEditor>; onBeginSelection: () => void }) {
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [saveFailed, setSaveFailed] = useState(false)
  const trigger = useRef<HTMLButtonElement | null>(null), popover = useRef<HTMLDivElement>(null)
  const selected = selection?.owner === controller && selection.menu === state.menu ? selection : null
  const editable = state.phase === 'ready' && state.activity === 'idle' && state.issue === null && !state.basket?.pendingRequestId
  const menu = state.menu && (!state.basket || state.menu.masterDataVersion === state.basket.sessionMasterDataVersion) ? state.menu : null
  const close = () => { setSelection(null); trigger.current?.focus() }
  useEffect(() => { if (selected?.stage === 'topping') popover.current?.focus() }, [selected?.stage, selected?.item])
  function change(group: Group, item: MenuItem, quantity: number) {
    setSelection(previous => {
      if (!previous) return previous
      const options = previous.options.filter(option => !(option.optionGroupId === group.optionGroupId && (group.maxSelections === 1 || option.sellableItemId === item.sellableItemId)))
      if (quantity !== 0) options.push({ optionGroupId: group.optionGroupId, sellableItemId: item.sellableItemId, quantity })
      const subitems = previous.item.optionGroups.filter(value => value.optionType === 'SubItem')
      const advance = group.optionType === 'SubItem' && quantity > 0 && subitems.length === 1 && group.maxSelections === 1 && validGroups(subitems, options)
      return { ...previous, options, stage: advance ? 'topping' : previous.stage }
    })
  }
  const line = selected && { sellableItemId: selected.item.sellableItemId, quantity: 1, options: selected.options, fulfillmentType: state.newFulfillment }
  const invalid = !line || !menu || line.options.some(option => !Number.isSafeInteger(option.quantity) || option.quantity < 1 || option.quantity > 2_147_483_647) || basketSelectionProblem({ lines: [line] }, menu) !== undefined
  return <section className="menu-selector panel" aria-label="เมนูอาหาร" aria-busy={state.activity === 'saving'}>
    <header><h2>เมนูอาหาร</h2><div className="menu-categories" aria-label="หมวดหมู่">ทั้งหมด</div><p>ราคาต่อหน่วยจากข้อมูลของโต๊ะ ไม่ใช่ยอดสุทธิของตะกร้า</p></header>
    {!menu && <p role="status" className="menu-empty">ยังอ่านเมนูที่ตรงกับโต๊ะไม่ได้ กรุณารีเฟรชตะกร้า</p>}
    {menu?.items.length === 0 && <p className="menu-empty">ไม่มีเมนูในข้อมูลของโต๊ะ</p>}
    <div className={`menu-columns ${selected?.item.optionGroups.some(group => group.optionType === 'SubItem') ? 'has-subitems' : ''}`}>
      <div className="menu-rows" role="region" aria-label="รายการอาหาร" tabIndex={0}>
        {menu?.items.map(item => <button key={item.sellableItemId} className={`menu-row ${selected?.item === item ? 'selected' : ''}`} disabled={!editable || !isMenuItemAvailable(item)}
          aria-label={`เลือกเมนู ${item.displayName}`} onClick={event => {
            onBeginSelection()
            trigger.current = event.currentTarget; setSaveFailed(false)
            setSelection({ owner: controller, menu, item, options: [], stage: item.optionGroups.some(group => group.optionType === 'SubItem') ? 'subitem' : 'topping' })
          }}><span><strong>{item.displayName}</strong><small>{item.sellableItemId}</small></span>
          <span>{item.availability.soldOut ? <span className="badge cancelled">Sold Out</span> : !isMenuItemAvailable(item) ? 'ไม่พร้อมขาย' : money.format(item.unitPrice!)}</span></button>)}
      </div>
      {selected?.item.optionGroups.some(group => group.optionType === 'SubItem') && <section className="subitem-column" aria-label="เลือกรายการย่อย">
        <h3>เลือกรายการย่อย</h3><p>{selected.item.displayName}</p>
        <Choices groups={selected.item.optionGroups.filter(group => group.optionType === 'SubItem')} options={selected.options} change={change} disabled={!editable} />
        <button disabled={!editable || !validGroups(selected.item.optionGroups.filter(group => group.optionType === 'SubItem'), selected.options)}
          onClick={() => setSelection({ ...selected, stage: 'topping' })}>เลือก Topping</button>
      </section>}
    </div>
    {selected?.stage === 'topping' && <div ref={popover} className="topping-popover" role="dialog" aria-label="เลือก Topping" tabIndex={-1}
      onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); close() } }}>
      <header><h3>เลือก Topping</h3><button aria-label="ปิดตัวเลือก Topping" onClick={close}>×</button></header>
      <p>{selected.item.displayName}</p>
      <Choices groups={selected.item.optionGroups.filter(group => group.optionType === 'Topping')} options={selected.options} change={change} disabled={!editable} />
      {!selected.item.optionGroups.some(group => group.optionType === 'Topping') && <p>ไม่มี Topping สำหรับเมนูนี้</p>}
      {invalid && <p className="selection-rule" role="status">กรุณาเลือกให้ครบตามจำนวนที่กำหนด และใช้ตัวเลือกที่พร้อมขาย</p>}
      {saveFailed && <p role="alert">บันทึกลงตะกร้าไม่สำเร็จ กรุณาตรวจสอบสถานะตะกร้า</p>}
      <button className="add-to-basket" disabled={!editable || invalid} onClick={async () => {
        if (!line) return
        const saved = await controller.addLine({ sellableItemId: line.sellableItemId, quantity: line.quantity, options: line.options })
        if (saved) close(); else setSaveFailed(true)
      }}>ลงตะกร้า</button>
    </div>}
  </section>
}
