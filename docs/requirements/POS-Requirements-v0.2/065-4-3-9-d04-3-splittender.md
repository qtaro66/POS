
#### 4.3.9 D04.3 — SplitTender — ยืนยัน

- บิลเดียวรองรับหลายวิธีชำระ เช่นเงินสด 400 บาทและ QR 600 บาทสำหรับยอดรวม 1,000 บาท
- แต่ละส่วนเป็น Payment Line แยก มี `paymentLineId`, `paymentMethodId`, จำนวนเงิน, สถานะยืนยัน, ผู้ยืนยัน, เวลา และ reference/หมายเหตุถ้ามี
- เมื่อ Payment Line แรกได้รับการยืนยันแต่ยอดยังไม่ครบ ให้ session เป็น `PartiallyPaid` และ persist ข้อมูลทันทีเพื่อกู้คืนได้หลัง process/OS restart
- `CompletePayment` ทำได้เมื่อยอด Payment Line ที่มีผลรวมครบยอดที่ต้องชำระตามกฎเงินสด/เงินทอนที่ยังต้องกำหนด
- Payment Line ที่ยืนยันแล้วห้ามแก้หรือลบตรง ๆ หากบันทึกผิดต้องสร้าง reversal event; ต้องย้อนทุกรายการสำเร็จก่อนกลับ `PaymentPending` หรือ `Ordering`
- ระหว่าง `PartiallyPaid` ห้ามแก้สินค้า ส่วนลด ย้าย/แยกโต๊ะ หรือเพิ่มคำขอยกเลิก
- Payment Method เดียวกันใช้ได้สูงสุดหนึ่ง Payment Line ต่อบิล ตาม D04.6
