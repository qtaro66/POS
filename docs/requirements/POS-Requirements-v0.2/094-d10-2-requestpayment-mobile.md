
### D10.2 — `RequestPayment` จาก Mobile — ยืนยัน

- พนักงาน Active ที่ Login บน Mobile สามารถส่ง `RequestPayment` ได้โดยไม่ต้องกรอก PIN ซ้ำและไม่ต้องมีสิทธิ์พิเศษเพิ่มเติม
- ทำได้เฉพาะ table session สถานะ `Ordering` และต้องมี Order Line ที่ยังมีผลอย่างน้อยหนึ่งรายการ
- หาก Mobile เครื่องที่กดมี Draft ที่ยังไม่ Submit ต้องให้ผู้ใช้ส่ง Draft หรือทิ้ง Draft ก่อน ห้ามส่ง `RequestPayment` ข้าม Draft ของเครื่องนั้น
- Branch ไม่สามารถทราบ Draft ที่ยังอยู่บน Mobile เครื่องอื่น; หากเครื่องอื่นส่งภายหลังที่ session เป็น `PaymentPending` ให้ปฏิเสธทั้ง Batch และเก็บ Draft ไว้
- ต้องปฏิเสธ `RequestPayment` หากมี cancellation request ที่ยังไม่ปิดตาม D06
- เมื่อรับคำสั่ง Branch ต้องตรวจยอด/สถานะล่าสุด สร้าง Bill Preview Snapshot และ persistent print job ที่แคชเชียร์ แล้วเปลี่ยน session เป็น `PaymentPending` ภายใน transaction/outbox ที่สอดคล้องกัน
- Mobile แสดงผล `Accepted`, `Rejected` หรือ `Unknown` และสถานะโต๊ะล่าสุด; กรณี `Unknown` ต้อง Status Query ด้วย `requestId` เดิม
- Mobile ไม่มีสิทธิ์เลือกช่องทางชำระ กรอกยอด รับเงิน หรือยืนยัน Payment Line และไม่สามารถใช้ `CancelPaymentRequest`; การกลับไป `Ordering` ทำได้เฉพาะ PC POS
- `RequestPayment` ต้องมี `requestId` และเป็น idempotent; Retry หรือคำสั่งซ้ำต้องไม่สร้าง Preview Snapshot/print job ซ้ำ
