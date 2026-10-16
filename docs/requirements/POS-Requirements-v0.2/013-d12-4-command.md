
#### D12.4 Command และขอบเขตช่องทาง — ยืนยันเบื้องต้น

การกระทำภายใน Mobile Draft ซึ่งไม่ส่งเป็น Branch command แยก:

```text
AddOrderItem
ChangeQuantity
AddComment
```

คำสั่งจาก Mobile ไป Branch:

```text
OpenTable
SubmitOrder     # ส่ง Draft ทั้งชุดเป็น order batch เดียว
RequestPayment
```

คำสั่งที่ดำเนินการที่ PC เท่านั้น โดย PC ยังสามารถทำงานขายและเพิ่มออร์เดอร์โดยตรงได้:

```text
CancelOrderItem
CancelPaymentRequest
CompletePayment
```

ทุก Branch command ต้องมีอย่างน้อย `requestId`, `branchId`, `deviceId`, `employeeId`, `tableSessionId`, `clientCreatedAt` และ payload; Branch เพิ่ม identifier ของ record/batch, `branchSequence`, `serverReceivedAt` และสถานะ/เวอร์ชันผลลัพธ์ คำสั่งที่แก้ shared state แบบไม่ใช่ append ต้องมี expected state/version ตามชนิดคำสั่ง
