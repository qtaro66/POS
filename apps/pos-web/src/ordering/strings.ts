export const strings = {
  appTitle: 'POS สาขา', tables: 'โต๊ะที่เปิดอยู่', refresh: 'รีเฟรชข้อมูล', lock: 'ล็อกหน้าจอ',
  signInTitle: 'กรุณาเข้าสู่ระบบ POS', signInHint: 'เข้าสู่ระบบด้วยบัญชีพนักงานเพื่อดูข้อมูลโต๊ะ',
  preview: 'ตัวอย่างหน้าจอ — ไม่ใช่ข้อมูลจริง', partialFloorPlan: 'แสดงเฉพาะโต๊ะที่เปิดใช้งาน ไม่ใช่ผังโต๊ะทั้งหมด',
  loadingTables: 'กำลังอ่านข้อมูลโต๊ะ…', loadingDetail: 'กำลังอ่านรายการและยอดจากสาขา…',
  tablesError: 'อ่านข้อมูลโต๊ะไม่ได้ กรุณาตรวจสอบการเชื่อมต่อแล้วรีเฟรชอีกครั้ง',
  detailError: 'อ่านรายการไม่ได้ ยอดเดิมถูกซ่อนไว้ กรุณารีเฟรชข้อมูลอีกครั้ง',
  noTables: 'ไม่มีโต๊ะที่เปิดอยู่', chooseTable: 'เลือกโต๊ะเพื่อดูรายการที่ส่งแล้ว',
  notFound: 'ไม่พบโต๊ะที่กำลังใช้งาน กรุณารีเฟรชรายการโต๊ะ', sent: 'รายการที่ส่งแล้ว',
  noOrders: 'ยังไม่มีออร์เดอร์ที่สาขารับแล้ว', accepted: 'สาขารับออร์เดอร์แล้ว',
  dineIn: 'ทานที่ร้าน', takeaway: 'กลับบ้าน', cancelled: 'ยกเลิกแล้ว',
  cancellationPending: 'โต๊ะนี้มีรายการยกเลิกที่ยังอยู่ระหว่างดำเนินการ',
  noActiveBill: 'ยังไม่มีรายการที่คิดเงิน', estimate: 'ยอดประมาณการ ณ เวลาที่อ่านข้อมูล', previewTotal: 'ยอดตรวจสอบรายการ',
  notReceipt: 'ไม่ใช่ใบเสร็จหรือยอดรับชำระ', itemGross: 'ยอดสินค้า', lineDiscount: 'ส่วนลดรายการ',
  billDiscount: 'ส่วนลดท้ายบิล', serviceCharge: 'Service Charge', vatIncluded: 'VAT ที่รวมอยู่',
  rounding: 'ปรับเศษ', batchNet: 'ยอดรายการหลังส่วนลด',
  batchNote: 'ยอดรอบส่งยังไม่รวมส่วนลดท้ายบิล Service Charge และการปัดเศษ',
  quantity: 'จำนวน', unitPrice: 'ราคาต่อหน่วย', readAt: 'อ่านข้อมูลเมื่อ', round: 'รอบส่ง', batches: 'รอบออร์เดอร์ที่สาขารับแล้ว',
} as const
export const sessionLabels: Record<string, string> = {
  Ordering: 'กำลังสั่ง', PaymentPending: 'รอชำระ', PaymentInProgress: 'กำลังรับชำระ', PartiallyPaid: 'ชำระบางส่วน',
}
