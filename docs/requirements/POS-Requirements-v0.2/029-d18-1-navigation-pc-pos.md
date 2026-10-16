
#### D18.1 — Navigation หลักและหน้าโต๊ะของ PC POS — ยืนยัน

- Navigation หลักของ PC POS มี `หน้าหลัก`, `โต๊ะ`, `ประวัติธุรกรรม`, `ร้านและกะ` และ `สถานะระบบ`
- หน้า `โต๊ะ` ใช้ Split View ในหน้าเดียว: ด้านหนึ่งแสดงโซนและผังโต๊ะ อีกด้านแสดง Order Line, Topping, หมายเหตุ ยอด และคำสั่งของโต๊ะที่เลือก โดยไม่ต้องเปลี่ยนไปหน้า Payment แยก
- Workflow เพิ่มรายการ ย้ายโต๊ะ/รายการ แยกรายการ/บิล ส่วนลด หมายเหตุ ตรวจยกเลิก และชำระเงินต้องอยู่ภายใต้ `tableSessionId` ของโต๊ะที่เลือก
- Payment ไม่เป็น Navigation หลักแยกต่างหาก และระบบมีช่องทางขายเฉพาะโต๊ะตาม CORE-04 จึงไม่มี TOGO/Delivery ในหน้า PC POS
- ไม่แสดงค่าใช้จ่าย สต็อก รายงานรวมหลายสาขา การจัดการเมนู/Master Data การตั้งค่าส่วนกลาง ผู้ใช้ส่วนกลาง Refund หรือการยกเลิกบิลที่ชำระแล้วใน Navigation หลัก งานเหล่านี้อยู่ใน Back Office หรือขอบเขตหน้าที่แยกตามข้อกำหนดของแต่ละฟังก์ชัน
- `หน้าหลัก` แสดงสถานะร้าน/กะ Notify สถานะ Online/Offline เวลาซิงก์ล่าสุด และงานค้างที่ต้องดำเนินการ
- `ประวัติธุรกรรม` ใช้กฎ D01.4–D01.4.1; การเปิดหน้าไม่ต้องใช้ PIN และการ Reprint ยังตรวจสิทธิ์ตามประเภทเอกสาร
- `ร้านและกะ` แสดงสถานะปัจจุบันและคำสั่ง `OpenStore`, `OpenShift`, `CloseShift`, `CloseStore`; การเปิดดูหน้าไม่ต้องใช้ PIN แต่การดำเนินคำสั่งใช้กฎ D04.11.5
- `สถานะระบบ` แสดง Branch Service, Database, Cloud Sync, PrintGateway และ Print Job ผิดปกติแบบ Read-only สำหรับผู้ใช้ทั่วไป ส่วน Retry/Reprint/ทดสอบลิ้นชัก/แก้ Printer Profile ต้องตรวจสิทธิ์ของคำสั่งนั้นแยกต่างหาก

ปุ่มล็อก/ปลดล็อกโต๊ะเป็น UI ของ Payment State Machine ไม่สร้างฟิลด์ `isLocked` หรือสถานะล็อกคู่ขนาน:

- เมื่อ session เป็น `Ordering` ให้แสดง `ล็อกโต๊ะเพื่อชำระ`; คำสั่งนี้มีความหมายเป็น `RequestPayment` และเมื่อ Branch commit สำเร็จจึงเปลี่ยนเป็น `PaymentPending`
- เมื่อ session เป็น `PaymentPending` และยังไม่มี Payment Line ที่ยืนยัน ให้แสดง `ปลดล็อกเพื่อรับออร์เดอร์เพิ่ม`; คำสั่งนี้มีความหมายเป็น `CancelPaymentRequest` และเมื่อ commit สำเร็จให้กลับเป็น `Ordering`
- การปลดล็อกต้องยกเลิก Bill Preview/Payment Snapshot ที่ยังไม่เริ่มรับเงิน แต่คง `tableSessionId`, `sessionMasterDataVersion`, Order Line, ส่วนลดรายการ และส่วนลดท้ายบิลเดิม จากนั้น Mobile จึงส่ง Draft/Order Batch เพิ่มได้อีก
- ทำได้เฉพาะ PC POS ไม่บังคับกรอกเหตุผล และต้องบันทึกผู้ดำเนินการ เวลา Branch, device/terminal, สถานะก่อน–หลัง และ `tableSessionId` ใน audit log
- `PaymentInProgress` หรือ `PartiallyPaid` ปลดล็อกโดยตรงไม่ได้ ต้อง Reverse Payment Line ที่ยังมีผลทั้งหมดตามสิทธิ์ `ReversePayment` แล้วจึงยกเลิก Payment Flow ตาม D04; `Completed` ปลดล็อกไม่ได้
- ปุ่มและสิทธิ์ต้องใช้สถานะจริงจาก Branch Service ห้ามเก็บหรือสลับสถานะเฉพาะใน Browser
