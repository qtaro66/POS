
##### D01.5 — การพิมพ์ Bill Preview — ยืนยัน

- PC POS สั่งพิมพ์ Bill Preview ด้วยตนเองขณะ table session เป็น `Ordering` ได้โดยไม่ใช้ PIN และการพิมพ์ไม่เปลี่ยนสถานะโต๊ะ
- เมื่อ Branch commit `RequestPayment` และเปลี่ยน table session เป็น `PaymentPending` สำเร็จ ต้องสร้าง Bill Preview snapshot และ persistent print job ไปเครื่องแคชเชียร์หนึ่งงานใน transaction/outbox ที่สอดคล้องกัน
- `RequestPayment` ที่ส่งซ้ำด้วย `requestId` เดิมต้องคืนผลเดิมและห้ามสร้าง Preview snapshot/print job ซ้ำ
- Preview อัตโนมัติต้องใช้ข้อมูล Snapshot ณ transaction ที่รับ `RequestPayment` และแสดงรายการ จำนวน Topping หมายเหตุ ส่วนลด Charges VAT Rounding และยอดสุทธิ
- เอกสารต้องระบุ `ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ`, ไม่มีเลขใบเสร็จ และไม่สร้าง Payment, ยอดขาย, drawer event หรือสถานะ `Completed`
- ความล้มเหลวหรือ `Unknown` ของงานพิมพ์หลัง commit ไม่ย้อน `PaymentPending`; PC ต้องเห็นสถานะตาม D20 และสามารถสั่ง Reprint Preview ได้
- Reprint Preview สร้าง `printJobId` ใหม่ อ้างอิง Preview snapshot เดิม และระบุว่าเป็นสำเนา ห้ามคำนวณจากข้อมูลปัจจุบันใหม่
- การพิมพ์และ Reprint Preview ไม่ต้องใช้ PIN ไม่บังคับเหตุผล และไม่สร้าง Business Audit ของผู้สั่ง แต่ต้องเก็บ Print Job/attempt/status ตาม D20
- Bill Preview ส่งเฉพาะเครื่องแคชเชียร์ ห้ามสร้าง print job ไปครัว เค้ก หรือ Production Station
