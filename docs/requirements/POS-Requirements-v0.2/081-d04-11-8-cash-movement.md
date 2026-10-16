
##### D04.11.8 — Cash Movement ระหว่างวัน — ไม่ใช้

- ระบบไม่สร้างฟังก์ชัน `CashIn`, `CashOut`, `CashAdjustmentIncrease`, `CashAdjustmentDecrease` หรือการนำส่งเงินระหว่างที่ `storeSessionId` ยังเปิด
- การส่งมอบพนักงานระหว่างกะไม่เปลี่ยนจำนวนเงินจริงและไม่เปลี่ยน `expectedCashAmount`; `OpenShift`/`CloseShift` เป็นการเปลี่ยนผู้รับผิดชอบ ไม่ใช่การเคลื่อนย้ายเงิน
- ผลสุทธิจากการรับและทอนเงินลูกค้าใช้ Cash `PaymentLine.appliedAmount` ที่ commit และยังมีผล โดย `tenderedAmount - changeAmount` ต้องเท่ากับ applied amount ของรายการนั้น
- การเปิดลิ้นชักด้วยตนเองตาม D04.8 ไม่สร้าง Cash Movement และไม่เปลี่ยน expected; ใช้ PIN ที่ได้รับสิทธิ์และสร้าง audit log ทุกครั้ง แต่ระบบไม่อ้างว่าสามารถตรวจว่ามีการนำเงินจริงออกหรือไม่
- สูตร expected ไม่รวม cash refund เพราะการคืนเงินหลังชำระเป็นกระบวนการ Manual ของส่วนกลางนอก Workflow ของ POS ตาม D04.12 และไม่มีการจ่ายจากลิ้นชักสาขา
