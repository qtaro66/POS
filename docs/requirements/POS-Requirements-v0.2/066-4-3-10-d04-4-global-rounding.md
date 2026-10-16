
#### 4.3.10 D04.4 — Global Rounding — ยืนยัน

- ปัดเศษสตางค์ลงทุกช่องทาง โดย `amountDue = floor(netAmount)` เป็นจำนวนเงินบาทเต็ม
- คำนวณ VAT และยอดก่อนปัดจากสูตร D05 ก่อน จากนั้นเก็บผลต่างเป็น `roundingAdjustment = amountDue - netAmount`; ห้ามบันทึกเป็นส่วนลด
- สำหรับ SplitTender ให้ปัดยอดบิลหนึ่งครั้งก่อนแบ่งยอด และผลรวม Payment Line ทุกช่องทางต้องเท่ากับ `amountDue` ห้าม floor แต่ละ Payment Line เพราะจะลดเศษซ้ำ
- ตัวอย่าง `netAmount = 100.90` ได้ `roundingAdjustment = -0.90` และ `amountDue = 100.00` ไม่ว่าจะชำระเงินสด QR โอน บัตร Custom หรือหลายช่องทางร่วมกัน
- Payment Line ที่ยืนยันแล้วอ้างอิง calculation snapshot และยอดหลังปัดเดียวกัน ห้ามคำนวณ rounding ใหม่ระหว่างรับชำระ
