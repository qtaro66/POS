export const basketPanelStrings = {
  title: 'ตะกร้า', refresh: 'อ่านข้อมูลล่าสุด', loading: 'กำลังอ่านตะกร้าและข้อมูลโต๊ะ',
  empty: 'ยังไม่มีรายการในตะกร้า เลือกเมนูเพื่อเพิ่มรายการ', dineIn: 'ทานที่ร้าน', takeaway: 'กลับบ้าน',
  next: 'รูปแบบสำหรับรายการถัดไป', comment: 'แก้หมายเหตุ', applyComment: 'ใช้หมายเหตุ', remove: 'ลบรายการ',
  saving: 'กำลังบันทึกตะกร้า', submitting: 'กำลังตรวจสอบและส่งออเดอร์', recovering: 'กำลังตรวจสอบผลกับสาขา', retrying: 'กำลังตรวจสอบคำขอเดิมก่อนส่ง',
  check: 'ตรวจสอบผลคำขอเดิม', retry: 'ตรวจสอบและลองส่งคำขอเดิม', pending: 'ยังไม่ทราบผลยืนยัน ห้ามสร้างออร์เดอร์ใหม่แทนคำขอเดิม',
  accepted: 'สาขารับออร์เดอร์แล้ว', rejected: 'สาขาปฏิเสธออร์เดอร์ โปรดตรวจสอบตะกร้าก่อนส่งใหม่',
  localWarning: 'มีผลยืนยันจากสาขาแล้ว แต่การบันทึกในเครื่องยังไม่สมบูรณ์ โปรดตรวจสอบคำขอเดิมอีกครั้ง',
  unitPrice: 'ราคาต่อหน่วย', confirmedTotals: 'ดูยอดที่สาขายืนยันในรายการส่งแล้ว',
  editError: 'ไม่สามารถแก้รายการนี้ได้ โปรดตรวจจำนวนและตัวเลือกสินค้า',
} as const

export const editorIssueLabels: Record<string, string> = {
  storageUnavailable: 'ไม่สามารถอ่านหรือบันทึกตะกร้าในเครื่องได้ กรุณาลองอ่านข้อมูลใหม่',
  sessionUnavailable: 'ยังตรวจสอบโต๊ะกับสาขาไม่ได้ ตะกร้าเดิมยังคงเก็บไว้',
  sessionLocked: 'โต๊ะนี้ยังไม่อนุญาตให้เพิ่มหรือแก้รายการ',
  catalogUnavailable: 'ยังอ่านเมนูของโต๊ะนี้ไม่ได้ ตะกร้าเดิมยังคงเก็บไว้',
  catalogChanged: 'ข้อมูลเมนูไม่ตรงกับตะกร้าเดิม กรุณาตรวจสอบก่อนดำเนินการต่อ',
  localWriteFailed: 'ตะกร้ามีการเปลี่ยนแปลงหรือบันทึกไม่สำเร็จ กรุณาอ่านข้อมูลใหม่',
  refreshRequired: 'กรุณาอ่านข้อมูลล่าสุดก่อนเพิ่มหรือแก้รายการต่อ',
}
export const submissionBlockLabels: Record<string, string> = {
  busy: 'กำลังดำเนินการกับตะกร้านี้', cancelled: 'หยุดการตรวจสอบแล้ว โปรดอ่านข้อมูลล่าสุด',
  storageUnavailable: editorIssueLabels.storageUnavailable, staleBasket: editorIssueLabels.localWriteFailed,
  missingBasket: 'ไม่พบตะกร้าที่ตรงกับคำขอนี้ กรุณาอ่านข้อมูลล่าสุด', emptyBasket: 'ยังไม่มีรายการสำหรับส่ง',
  sessionUnavailable: editorIssueLabels.sessionUnavailable, sessionLocked: editorIssueLabels.sessionLocked,
  catalogUnavailable: editorIssueLabels.catalogUnavailable, catalogChanged: editorIssueLabels.catalogChanged,
  selectionUnavailable: 'สินค้าหรือตัวเลือกบางรายการยังไม่พร้อมขาย โปรดตรวจสอบตะกร้า',
  invalidSelection: 'ตัวเลือกหรือจำนวนบางรายการไม่ถูกต้อง โปรดตรวจสอบตะกร้า',
  localWriteFailed: editorIssueLabels.localWriteFailed, invalidRequest: 'ยังเตรียมออร์เดอร์ไม่ได้ โปรดตรวจสอบรายการและจำนวน',
}
