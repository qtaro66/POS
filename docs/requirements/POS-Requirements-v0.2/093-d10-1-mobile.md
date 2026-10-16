
### D10.1 — ข้อมูลยอดเงินที่ Mobile มองเห็น — ยืนยัน

- Mobile ดูรายการในโต๊ะ จำนวน ราคา Topping หมายเหตุ ส่วนลดที่มีผล ยอดสินค้า ส่วนลดท้ายบิล Service Charge, VAT ที่รวมอยู่ และยอดสุทธิปัจจุบันได้แบบ Read-only
- ยอดทั้งหมดต้องคำนวณและส่งจาก Branch Service; Mobile ห้ามคำนวณยอดอ้างอิงเอง
- ระหว่าง `Ordering` ต้องระบุว่ายอดเป็นยอดประมาณการปัจจุบัน เพราะยังเปลี่ยนได้จากการเพิ่ม/ย้ายรายการ ส่วนลด หรือ Price Override
- Mobile ดูสถานะ `Ordering`, `PaymentPending`, `PaymentInProgress`, `PartiallyPaid` และ `Completed` ได้
- Mobile ห้ามแสดง `tenderedAmount`, เงินทอน, Payment Line และรายละเอียดช่องทางรับเงิน; ข้อมูลเหล่านี้เป็นขอบเขตของ PC POS
- Card Charge ยังไม่แสดงบน Mobile เพราะจำนวนจริงคำนวณเมื่อ PC สร้าง Payment Line ตามช่องทางและจำนวนที่ชำระ
- เมื่อข้อมูลโต๊ะเปลี่ยน Mobile ต้อง Refresh ยอดจาก Branch ด้วย `tableVersion`; ห้ามใช้ยอด Cache เก่าเป็นยอดแจ้งลูกค้า
- Mobile ไม่มีสิทธิ์แก้ราคา ใส่ส่วนลด รับเงิน หรือยืนยันการชำระ
