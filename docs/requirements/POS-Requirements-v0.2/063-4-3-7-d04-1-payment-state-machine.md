
#### 4.3.7 D04.1 — Payment State Machine — ยืนยัน

```text
Ordering
  ↓ RequestPayment
PaymentPending
  ├── CancelPaymentRequest ──→ Ordering
  ↓ StartPayment โดย PC POS
PaymentInProgress
  ├── PaymentFailed / CancelPayment ──→ PaymentPending
  └── CompletePayment ──→ Completed ──→ โต๊ะ Available
```

- `RequestPayment` ต้องผ่านเงื่อนไข D06 ว่าไม่มี cancellation request ค้าง และเมื่อ commit แล้วต้อง freeze รายการ ราคา ส่วนลด ค่าธรรมเนียม VAT และ calculation policy
- `StartPayment`, `CancelPayment` และ `CompletePayment` ทำที่ PC POS เท่านั้น
- หากมีเงินบางส่วนถูกรับหรือยืนยันจากช่องทางภายนอกแล้ว ห้ามกลับ `Ordering` จนกว่าจะบันทึกการย้อนรายการรับเงินสำเร็จ
- D04.3 ยืนยันการรับหลายวิธีในบิลเดียวแล้ว จึงเปิดใช้ `PartiallyPaid` สำหรับกรณีมี Payment Line ที่ยืนยันแล้วแต่ยอดยังไม่ครบ
- หลัง `CompletePayment` ห้ามแก้ session เดิมและโต๊ะเปลี่ยนเป็น `Available` อัตโนมัติ
