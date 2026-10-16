
##### D05.8 — ฐานและ VAT ของ Card Charge — ยืนยัน

- ค่าเริ่มต้นเป็น 0% และปิดใช้งาน เปิดใช้ได้เฉพาะ Payment Method ที่ส่วนกลางกำหนด `cardChargeApplicable = true`
- พนักงานกรอก `principalAppliedAmount` ซึ่งเป็นเงินต้นที่ Payment Line นำไปตัดยอดบิลก่อน Card Charge; ระบบคำนวณ `cardChargeAmount` จากเงินต้นของ Payment Line นั้น ไม่ใช่จากยอดทั้งบิล
- กรณี Split Payment หรือใช้บัตรหลายใบ ต้องคำนวณ Card Charge แยกตามแต่ละ Payment Line และอัตราที่ snapshot สำหรับ Payment Method นั้น
- ฐานของ Card Charge คือ `principalAppliedAmount`; ห้ามนำ Service Charge มาเพิ่มเป็นฐานซ้ำและห้ามคิด Card Charge ทบ Card Charge
- Card Charge ที่เรียกจากลูกค้าเป็นยอดรวม VAT ตามอัตราที่ snapshot ของบิล ระบบต้องแยกส่วนมูลค่าก่อน VAT และ VAT เพื่อรายงานโดยไม่บวก VAT เพิ่มจากยอด Card Charge อีกครั้ง
- Card Charge ที่ร้านเรียกจากลูกค้าต้องเก็บแยกจาก Merchant Fee ที่ธนาคารหรือผู้ให้บริการรับชำระหักจากร้านค้า
- ใน Payment Line ให้เก็บอย่างน้อย `principalAppliedAmount`, `cardChargeRate`, `cardChargeAmount`, `roundingAllocation` และ `appliedAmount`; โดย `appliedAmount` เป็นยอดที่รับผ่านช่องทางนั้นหลังรวม Card Charge และผลปัดเศษที่จัดสรรให้ line
- ระบบต้องแสดงทั้งยอดที่นำไปตัดบิลและยอดที่ต้องเรียกเก็บผ่านเครื่องรับบัตรอย่างชัดเจน ห้ามให้ระบบอนุมานเงินต้นย้อนกลับจากยอดรวมที่พนักงานกรอก
- เก็บ Card Charge แยกจากยอดสินค้า ส่วนลด Service Charge และ `roundingAdjustment` ใน bill/payment snapshot และรายงาน
- อัตราและกฎ Card Charge ต้องเป็น versioned/effective-dated และ snapshot ตอนเข้า `PaymentPending`; จำนวน Card Charge คำนวณเมื่อสร้าง Payment Line และ freeze เมื่อยืนยัน Payment Line นั้น
- ก่อนยืนยัน Payment Line หากเปลี่ยน Payment Method หรือ `principalAppliedAmount` ต้องคำนวณ Card Charge ใหม่
- หาก Reverse Payment Line ต้อง Reverse Card Charge ที่ผูกกับ Payment Line เดียวกันทั้งหมด; ห้าม Card Charge ติดลบ
- ก่อนเปิดใช้งานจริงต้องยืนยันเงื่อนไขการเรียกเก็บกับธนาคาร/ผู้ให้บริการรับบัตรและวิธีลงบัญชีกับผู้รับผิดชอบด้านบัญชี
