
##### D01.4.1 — Reprint Kitchen Order จากหน้าประวัติ — ยืนยัน

- ทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ReprintKitchenOrder` ทุกครั้งที่สั่งพิมพ์
- พิมพ์ซ้ำได้ครั้งละหนึ่ง Order Line ที่เคย `SubmitOrder` และมี original kitchen/production print snapshot เท่านั้น ไม่มีคำสั่ง Reprint ทั้งบิลหรือทั้ง `tableSessionId`
- Reprint ต้องใช้ immutable snapshot เดิมของ Order Line รวมจำนวน Topping หมายเหตุ เลขโต๊ะ และ Production Station ณ เวลาที่ส่งเดิม ห้ามดึง Master Data ปัจจุบันหรือเปลี่ยน payload เดิม
- ใบที่พิมพ์ซ้ำต้องระบุ `สำเนา / พิมพ์ซ้ำ` อย่างชัดเจน และส่งเฉพาะ Production Station เดิมของรายการนั้น
- ต้องสร้าง `printJobId` ใหม่ที่อ้างอิง original `printJobId`, `orderBatchId`, `tableSessionId` และ order-line reference; การ Reprint ไม่สร้าง Order ใหม่ ไม่ตัดสต็อกซ้ำ และไม่เปลี่ยนยอดบิล
- ทุกคำสั่ง Reprint ต้องบันทึก employee ID, เวลา Branch, device/terminal, รายการ สถานี original/reprint identifiers และผลการพิมพ์ใน audit log; การเปิดดูประวัติยังคงไม่สร้าง audit ตาม D01.4
- ตรวจ PIN/สถานะ/สิทธิ์จากค่าล่าสุดที่ Branch Service ซิงก์ไว้ตาม D11 และใช้กฎสถานะงานพิมพ์/`Unknown` ตาม D20
