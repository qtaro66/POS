
##### D05.7 — ฐานคำนวณ Service Charge — ยืนยัน

- ค่าเริ่มต้นของ Service Charge เป็น 0%; หากเปิดใช้ภายหลังให้คำนวณจากยอดสินค้าและ Topping หลังหักส่วนลดระดับ Order Line และส่วนลดท้ายบิล
- ห้ามนำ Card Charge หรือ `roundingAdjustment` มาเป็นฐานคำนวณ Service Charge
- Service Charge เป็นยอดรวม VAT ตาม policy เดียวกับราคาอาหาร และต้องรวมอยู่ในยอดที่ใช้แยก VAT สำหรับรายงาน
- เมื่อรายการหรือส่วนลดเปลี่ยนก่อน `PaymentPending` ระบบต้องคำนวณฐานและจำนวน Service Charge ใหม่
- เมื่อเข้าสู่ `PaymentPending` ต้อง freeze ฐาน อัตรา จำนวน Service Charge และ policy version ที่ใช้
- อัตรา Service Charge ต้องรองรับทศนิยมและจัดเก็บแบบ versioned/effective-dated
