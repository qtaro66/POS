
### D19.2 — Throughput Target เบื้องต้น — ยืนยันจนกว่าจะทดสอบจริง

ฐานข้อมูลตัวอย่างปัจจุบันมี 8,156 บิลใน 31 วัน เฉลี่ยประมาณ 263 บิล/วัน และประมาณ 5.66 Product-detail records ต่อบิล จึงกำหนด Test Baseline ที่สูงกว่าค่าเฉลี่ยดังนี้ โดยยังไม่ถือเป็น Production Limit:

| รายการทดสอบ | เป้าหมายเบื้องต้น |
|---|---:|
| ปริมาณรายวัน | 1,000 บิล/สาขา/วัน |
| Mobile ส่ง `SubmitOrder` พร้อมกัน | 20 Batch |
| Order Line ต่อ Batch สำหรับ Load Test | 20 รายการ |
| Burst | 20 Batch ภายใน 10 วินาที |
| Sustained Load | 30 Batch/นาที ต่อเนื่อง 15 นาที |
| Payment | 10 `CompletePayment`/นาที |
| Print enqueue | รับงานจาก Burst ทั้งหมดเข้าคิวโดยไม่สูญหาย |
| Offline backlog | สะสมธุรกรรม 8 ชั่วโมงและส่งตามหลังได้ครบ |

เกณฑ์ผ่าน:

- ไม่มีคำสั่ง Order Line, Payment, Stock Movement, Audit Event, Print Job หรือ Cloud Outbox สูญหาย และยอดหลังจบการทดสอบต้องกระทบกันครบ
- `requestId`/idempotency key เดิมต้องคืนผลเดิมและห้ามสร้างผลซ้ำ; แต่ละ Batch ต้อง commit แบบ Atomic
- PC POS ต้องยังใช้งานได้ขณะ Central Sync ระบาย Backlog และการ Sync ต้องไม่แย่งทรัพยากรจน Response Time หลุด D19.1
- Print Job ต้องเข้าคิวครบแม้เครื่องพิมพ์จริงพิมพ์ไม่ทัน; สถานะรอคิวไม่ถือเป็น Failure เพียงเพราะอัตราทางกลของเครื่องพิมพ์ต่ำกว่าอัตรา enqueue
- ต้องทดสอบทั้ง Windows และ Linux pilot บน Hardware สาขาจริง บันทึก Peak จริง แล้วทบทวนค่าทั้งหมดก่อนกำหนด Production Capacity/Limit
