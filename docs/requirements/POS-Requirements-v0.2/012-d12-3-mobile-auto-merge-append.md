
#### D12.3 Mobile หลายเครื่องส่งออร์เดอร์เข้าโต๊ะเดียวกัน — ยืนยัน Auto-merge แบบ Append

- บน Mobile การเพิ่มสินค้า เปลี่ยนจำนวน และใส่หมายเหตุเป็นการแก้ Draft ภายในอุปกรณ์เดียวกัน ยังไม่เป็น command แยกที่ส่งเข้า Branch
- เมื่อผู้ใช้กดส่ง Mobile ส่ง `SubmitOrder` เพียงคำสั่งเดียว โดย payload มีรายการสินค้า จำนวน Topping และหมายเหตุทั้งหมดของ Draft ชุดนั้น
- เมื่อ Branch ตอบ `BranchAccepted` แล้ว order batch ชุดนั้นถือเป็น immutable สำหรับ Mobile หากต้องเพิ่มสินค้าให้สร้าง Draft/`SubmitOrder` ชุดใหม่ ส่วนการยกเลิกรายการที่ส่งแล้วทำที่ PC
- **ยืนยันใช้ทางเลือก 3 — รวมอัตโนมัติ:** Mobile หลายเครื่องส่ง order batch คนละชุดเข้าโต๊ะเดียวกันพร้อมกันได้ Branch ต้อง serialize การรับและ append ทุก batch ที่ผ่าน validation เข้า bill/table session เดียวกันโดยอัตโนมัติ
- การรวมอัตโนมัติหมายถึงรวมผลเพื่อแสดงและคำนวณเป็นบิลเดียว แต่ห้ามรวม/เขียนทับ field ภายใน batch เดิม แต่ละ batch และแต่ละ line ต้องรักษาขอบเขตและที่มาของตนเอง
- ทุก batch ต้องมี `orderBatchId`, `requestId`, `deviceId`, `employeeId`, `tableSessionId`, เวลาจาก Mobile, `serverReceivedAt` และ `branchSequence`; ทุก line ต้องมี `orderLineId` ที่ unique เพื่อ trace back ได้
- `SubmitOrder` ทุกครั้งต้องมี `requestId` แบบ unique เพื่อป้องกัน batch เดิมถูก append ซ้ำ
- ทุกโต๊ะที่เปิดต้องมี `tableSessionId`; `SubmitOrder` ต้องอ้าง session นี้ และ Branch ต้องปฏิเสธหากโต๊ะถูกปิด ย้าย session หรือเปิด session ใหม่แล้ว เพื่อไม่ให้ Draft เก่าเข้าโต๊ะรอบใหม่
- ไม่ใช้ `orderVersion` conflict กับการ append order batch ปกติ Branch กำหนดลำดับด้วย `branchSequence` และคืน `tableVersion` ล่าสุดเพื่อให้ Mobile refresh มุมมอง
- เมื่อ `BranchAccepted` แล้ว Mobile แก้ไขหรือลบ batch เดิมไม่ได้ การยกเลิกรายการทำได้เฉพาะ PC โดยสร้าง cancellation event ที่อ้าง `orderBatchId`/`orderLineId`; ห้าม physical delete
- Audit/traceability ต้องค้นย้อนจากบิล → batch → line → request → device/employee และจาก `requestId` กลับมายังผลลัพธ์เดิมได้
- เมื่อ Branch รับ `RequestPayment` สำเร็จ ต้องเปลี่ยน table session จาก `Ordering` เป็น `PaymentPending` ภายใน database transaction เดียวกัน และตอบ `BranchAccepted` หลัง commit เท่านั้น
- ตั้งแต่สถานะ `PaymentPending` Branch ต้องปฏิเสธ `SubmitOrder` ใหม่จากทั้ง Mobile และ PC ด้วยผลลัพธ์ที่ระบุว่าโต๊ะกำลังชำระเงิน; Draft บน Mobile ต้องคงอยู่เพื่อให้ผู้ใช้ส่งใหม่ได้หาก PC เปิดรับออร์เดอร์อีกครั้ง
- หาก `SubmitOrder` และ `RequestPayment` มาถึงพร้อมกัน Branch ต้อง serialize ตามลำดับที่ commit: ถ้า `SubmitOrder` commit ก่อน ให้นับรายการนั้นในยอดชำระ; ถ้า `RequestPayment` commit ก่อน ให้ปฏิเสธ `SubmitOrder` โดยห้าม append บางส่วน
- การออกจาก `PaymentPending` ทำได้สองทางเท่านั้น: PC ใช้ `CompletePayment` เพื่อปิดบิล หรือ PC ใช้ `CancelPaymentRequest` เพื่อกลับเป็น `Ordering` ก่อนเพิ่ม/รับออร์เดอร์ใหม่ ทุกการเปลี่ยนสถานะต้องมี audit event
- `RequestPayment`, `CancelPaymentRequest` และ `CompletePayment` ต้องรองรับ idempotency ด้วย `requestId`; คำขอซ้ำต้องคืนผลเดิมโดยไม่เปลี่ยนสถานะหรือบันทึกธุรกรรมซ้ำ
