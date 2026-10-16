
### 3.6 D20 — State Machine, identifier และ recovery — ยืนยัน

D20 แยก state machine อย่างน้อยสามชุด ห้ามรวมสถานะการส่งคำขอ สถานะโต๊ะ และสถานะงานพิมพ์เป็นค่าเดียว เพราะแต่ละส่วนสำเร็จหรือล้มเหลวแยกกันได้

#### D20.1 Mobile Request

```text
Draft → Pending → Submitted
                      ├──→ BranchAccepted
                      ├──→ Rejected
                      └──→ Unknown ──status query──→ BranchAccepted / Rejected / Pending
```

- `Draft` อยู่บน Mobile และยังไม่เริ่มส่ง; `Pending` อยู่ใน persistent local outbox; `Submitted` เริ่มส่งแล้วแต่ยังไม่มีผลยืนยัน
- `BranchAccepted` เกิดเมื่อ Branch commit คำสั่งและ outbox ที่เกี่ยวข้องลง persistent storage สำเร็จแล้ว
- `Rejected` ใช้เมื่อ Branch ปฏิเสธอย่างชัดเจน เช่น table session อยู่ใน `PaymentPending`; Draft/ข้อมูลต้นทางบน Mobile ต้องไม่สูญหาย
- `Unknown` หมายถึง Mobile ไม่ทราบผลหลังเริ่มส่ง ไม่ใช่สถานะธุรกรรมจริง ต้อง query ด้วย `requestId` เดิมและห้ามสร้างออร์เดอร์ใหม่
- `AddOrderItem`, `ChangeQuantity`, `AddComment` และ `SubmitOrder` จากการกดส่งหนึ่งครั้งเป็น `SubmitOrder` batch เดียว Branch ต้องรับหรือปฏิเสธทั้งชุด ห้าม partial commit

#### D20.2 Table Session

```text
Available ──OpenTable──→ Ordering
Ordering ──SubmitOrder──→ Ordering
Ordering ──ChangeTable (PC; ปลายทาง Available)──→ Ordering [คง tableSessionId]
Ordering ──RequestPayment──→ PaymentPending
Ordering ──CloseTable (PC; ไม่มีรายการ/ยอดค้าง)──→ Closed
PaymentPending ──CancelPaymentRequest (PC)──→ Ordering
PaymentPending ──StartPayment (PC)──→ PaymentInProgress
PaymentInProgress ──PaymentFailed / CancelPayment──→ PaymentPending
PaymentInProgress ──ยืนยัน Payment Line แต่ยอดยังไม่ครบ──→ PartiallyPaid
PartiallyPaid ──ยืนยันยอดที่เหลือครบ──→ Completed
PartiallyPaid ──ย้อน Payment Line ที่ยืนยันแล้วทั้งหมด──→ PaymentPending
PaymentInProgress ──CompletePayment (PC)──→ Completed
Ordering / PaymentPending ──CancelBill (ตามสิทธิ์)──→ Cancelled
```

- `SubmitOrder` รับได้เฉพาะ `Ordering`; หลัง Branch commit `RequestPayment` แล้วต้องปฏิเสธ `SubmitOrder` ที่มาภายหลังทั้ง batch
- `RequestPayment` ต้องถูกปฏิเสธหากยังมี cancellation request สถานะ `CancellationRequested`, `ProductionStatusChecking` หรือ `StatusRecorded` ที่ยังไม่ปิดด้วย `ConfirmCancellation`/`RejectCancellation`
- `CancelPaymentRequest` กลับไป `Ordering` ได้ก่อนเริ่มรับเงิน; เมื่อเป็น `PaymentInProgress` การล้มเหลวหรือยกเลิกการรับเงินให้กลับ `PaymentPending` ไม่กลับ `Ordering` โดยอัตโนมัติ
- D04.3 ยืนยัน SplitTender แล้ว จึงใช้ `PartiallyPaid` เมื่อมี Payment Line ที่ยืนยันแล้วแต่ยอดรวมยังไม่ครบ; ห้ามกลับ `Ordering` และห้ามลบ Payment Line ที่ยืนยันแล้ว ต้องใช้ reversal event
- เมื่อ `SubmitOrder` แข่งกับ `RequestPayment` ให้ผลตามลำดับ commit ของ Branch: batch ที่ commit ก่อนรวมในยอด ส่วน batch ที่ commit หลัง `PaymentPending` ถูกปฏิเสธ
- `ChangeTable` เปลี่ยนเฉพาะ `currentTableId` ของ session เดิม จึงไม่สร้าง session/บิลใหม่และไม่เปลี่ยนส่วนลดท้ายบิล; โต๊ะต้นทางเป็น `Available` เมื่อ commit สำเร็จ
- `TransferItems` ย้ายรายการข้าม `tableSessionId`; session ต้นทางและปลายทางยังคงเป็นคนละ session และใช้กฎส่วนลดของตนเอง
- หลัง `Completed` หรือ `Cancelled` ห้ามเพิ่ม/แก้รายการใน `tableSessionId` เดิม
- `CloseTable` ทำได้เฉพาะ PC POS เมื่อ session ไม่มีรายการคงเหลือและไม่มียอดค้างชำระ; เมื่อสำเร็จ session เป็น `Closed` และโต๊ะเป็น `Available` หากยังมีรายการต้องใช้กระบวนการยกเลิกบิลแทน
- การยกเลิกรายการสินค้าทำที่ PC เท่านั้น; การยกเลิกสินค้า/บิลสร้าง immutable cancellation event พร้อมผู้ดำเนินการ เวลา เหตุผล และข้อมูลอ้างอิง ห้ามลบหรือเขียนทับ payload ต้นฉบับ

#### D20.3 Print Job

```text
PrintQueued → Dispatching
                 ├──→ PrintDispatched
                 ├──→ Failed ──safe retry──→ PrintQueued
                 └──→ Unknown ──manual reprint──→ ReprintQueued
```

- มี persistent queue และ worker แบบ serialize แยกต่อเครื่องพิมพ์
- `Failed` ใช้เมื่อมีหลักฐานว่ายังไม่เริ่มส่งผลทางกายภาพ เช่นเชื่อมต่อไม่ได้ก่อนส่ง byte แรก จึง retry อัตโนมัติได้ตาม policy
- `Unknown` ใช้เมื่อ timeout/connection loss เกิดหลังเริ่มส่ง เพราะอาจมีกระดาษออกแล้ว ห้าม retry อัตโนมัติ
- การพิมพ์ใหม่จาก `Unknown` ต้องสั่งจาก PC สร้าง `reprintJobId`/print job ใหม่ที่อ้างอิงงานเดิม และบันทึกผู้สั่ง เหตุผล และเวลา
- Phase 0 ใช้ `PrintDispatched` เป็นสถานะอัตโนมัติสูงสุด การส่ง payload ลง TCP สำเร็จไม่ใช่หลักฐานว่ากระดาษออกจริง; พักการใช้ `Completed` ของงานพิมพ์จนกว่าจะทดสอบ status capability ของ Epson TM-T82 รุ่นจริง

#### D20.4 Identifier และ invariant

| Identifier | ขอบเขต |
|---|---|
| `tableSessionId` | รอบการใช้งานโต๊ะหนึ่งรอบ |
| `requestId` | คำสั่งหนึ่ง batch จาก Mobile และ idempotency ที่ Branch |
| `orderBatchId` | ชุดรายการ immutable ที่ Branch รับแล้ว |
| `printJobId` | เอกสารหนึ่งชุดต่อเครื่องพิมพ์หนึ่งเครื่อง |
| `printAttemptId` | ความพยายาม dispatch แต่ละครั้ง |
| `paymentId` | ธุรกรรมชำระเงิน |
| `cancellationId` | เหตุการณ์ยกเลิก |

- `requestId` ต้อง unique ภายในสาขา; การส่งค่าเดิมซ้ำต้องคืนผลเดิมและไม่สร้าง order batch ซ้ำ
- ออร์เดอร์หนึ่ง batch ที่ต้องออกหลายสถานีใช้ `requestId` เดียว แต่มี `printJobId` แยกต่อเครื่อง เพราะผลการส่งแต่ละเครื่องเป็นอิสระ
- การ commit ธุรกรรมธุรกิจ, audit record และ persistent outbox ที่จำเป็นต้องอยู่ใน transaction boundary เดียวกัน
- ทุก transition เก็บ Branch time, `branchSequence`, actor, device และเหตุผลตามที่เกี่ยวข้อง และต้องกู้สถานะที่ commit แล้วกลับได้หลัง process/OS restart
