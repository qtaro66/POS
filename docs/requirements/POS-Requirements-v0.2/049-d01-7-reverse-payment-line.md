
##### D01.7 — การ Reverse Payment Line — ยืนยัน

- ทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ReversePayment` ทุกครั้ง
- Reverse ได้เฉพาะ Payment Line ที่ยืนยันแล้วใน Payment Flow ที่ยังไม่ `Completed`; หลัง `CompletePayment` ห้าม Reverse ที่สาขาและต้องใช้กระบวนการ Refund ส่วนกลางตาม D04.12
- Reverse ได้เต็ม Payment Line เท่านั้น ไม่รองรับ Partial Reversal หากจำนวนหรือช่องทางผิดต้องสร้าง immutable Reversal Event ของรายการเดิมแล้วจึงสร้าง Payment Line ใหม่
- ห้ามแก้ ลบ หรือเขียนทับ Payment Line เดิม Reversal Event ต้องอ้างอิง original `paymentLineId` และมี identifier ของตนเอง
- หาก Reverse แล้วไม่เหลือ effective Payment Line ให้ table session กลับ `PaymentPending`; หากยังมียอดที่ยืนยันแล้วเหลืออยู่ให้คง `PartiallyPaid`
- สำหรับ QR, โอน, Credit Card และ Debit Card ระบบบันทึกผลแบบ Manual เท่านั้น ผู้ดำเนินการต้องจัดการการยกเลิกกับธนาคาร/EDC/ช่องทางภายนอกเอง ระบบไม่อ้างว่า External Reversal สำเร็จอัตโนมัติ
- การ Reverse เงินสดระหว่าง Payment Flow เป็นการคืนเงินรับที่บิลยังไม่ Completed ไม่ใช่ Refund หลังชำระ และไม่สร้าง Cash Movement แยก; effective Cash `appliedAmount` ต้องตัดรายการที่ถูก Reverse ออกจากสูตร expected cash
- ช่องเหตุผล/หมายเหตุเป็น optional ไม่บังคับกรอก
- Audit ต้องเก็บ original Payment Line/Reversal Event, ช่องทาง, จำนวน, employee ID, เวลา Branch, device/terminal และเหตุผลเมื่อมี โดยห้ามเก็บ PIN แบบ plaintext
- สถานะ Active/PIN verifier/สิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11
